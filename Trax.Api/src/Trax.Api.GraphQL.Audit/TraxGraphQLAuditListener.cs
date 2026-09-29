using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// HotChocolate <see cref="ExecutionDiagnosticEventListener"/> that captures
/// per-request audit entries and enqueues them to <see cref="TraxAuditChannel"/>.
/// Non-blocking, swallows all exceptions: a misbehaving sink or redactor must
/// never crash a GraphQL request.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxGraphQLAuditListener(
    IHttpContextAccessor httpContextAccessor,
    TraxAuditChannel channel,
    IOptions<TraxAuditOptions> options,
    ITraxAuditRedactor redactor,
    TimeProvider timeProvider,
    ILogger<TraxGraphQLAuditListener> logger
) : ExecutionDiagnosticEventListener
{
    /// <summary>
    /// Key under which <see cref="RequestError(RequestContext, Exception)"/> parks the
    /// request-level exception for the scope to pick up on completion. The listener is a
    /// singleton, so per-request state has to live on the request context.
    /// </summary>
    private const string ExceptionKey = "Trax.Audit.RequestException";

    private readonly TraxAuditOptions _options = options.Value;

    /// <inheritdoc />
    public override IDisposable ExecuteRequest(RequestContext context)
    {
        try
        {
            var startTicks = timeProvider.GetTimestamp();
            var startTime = timeProvider.GetUtcNow();
            var principal = CapturePrincipal();

            return new RequestScope(this, context, startTicks, startTime, principal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to start capture. Skipping request.");
            return EmptyScope;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// HotChocolate 16 no longer hangs the request-level exception off the context, so the
    /// listener records it here and reads it back when the scope completes.
    /// </remarks>
    public override void RequestError(RequestContext context, Exception exception)
    {
        try
        {
            context.ContextData[ExceptionKey] = exception;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to record a request exception.");
        }
    }

    /// <summary>
    /// Both skips need the compiled operation, which exists only partway through the pipeline,
    /// so they are decided when the scope completes rather than when it opens. A request that
    /// never produced an operation (a parse or validation failure) is audited.
    /// </summary>
    private bool ShouldSkipOnComplete(RequestContext context)
    {
        if (!_options.SkipIntrospection && !_options.SkipSubscriptions)
            return false;

        if (!context.TryGetOperation(out var operation))
            return false;

        if (_options.SkipSubscriptions && operation.Kind == OperationType.Subscription)
            return true;

        return _options.SkipIntrospection && SelectsOnlyIntrospection(operation.Definition);
    }

    /// <summary>
    /// True when every top-level selection of the executed operation is <c>__schema</c>,
    /// <c>__type</c> or <c>__typename</c>. A fragment spread or inline fragment at the top level
    /// is not treated as introspection, so the request is audited.
    /// </summary>
    private static bool SelectsOnlyIntrospection(OperationDefinitionNode definition)
    {
        var selections = definition.SelectionSet.Selections;
        if (selections.Count == 0)
            return false;

        foreach (var selection in selections)
        {
            if (selection is not FieldNode field)
                return false;

            var name = field.Name.Value;
            if (name is not ("__schema" or "__type" or "__typename"))
                return false;
        }

        return true;
    }

    private (string Id, string? Type) CapturePrincipal()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || !user.TryGetPrincipalId(out var id))
            return (_options.DefaultPrincipalId, null);

        var type = user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value;
        return (id, type);
    }

    private void CompleteScope(
        RequestContext context,
        long startTicks,
        DateTimeOffset startTime,
        (string Id, string? Type) principal
    )
    {
        try
        {
            if (ShouldSkipOnComplete(context))
                return;

            var elapsed = timeProvider.GetElapsedTime(startTicks);
            var document = CaptureDocument(context);
            var variables = BuildVariables(context);
            var redactedVariables = SafeRedact(variables);
            var (success, errorText) = InterpretResult(context);

            var entry = new TraxAuditEntry(
                PrincipalId: principal.Id,
                PrincipalType: principal.Type,
                OperationName: context.Request.OperationName,
                Document: document,
                Variables: redactedVariables,
                DurationMs: (long)elapsed.TotalMilliseconds,
                Timestamp: startTime,
                Success: success,
                ErrorText: errorText,
                Metadata: null
            );

            channel.TryEnqueue(entry);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to build entry. Skipping.");
        }
    }

    /// <summary>
    /// Returns the document as sent, or, past <see cref="TraxAuditOptions.MaxDocumentLength"/>,
    /// its head followed by every field the compiled operation executes. The head alone can be
    /// filled by padding placed ahead of the fields that matter; the field list cannot, because
    /// it is read from the compiled operation after fragment expansion and holds each schema
    /// coordinate once, so its size is bounded by the schema rather than by the request.
    /// </summary>
    private string CaptureDocument(RequestContext context)
    {
        var document = context.OperationDocumentInfo.Document?.ToString() ?? string.Empty;
        if (document.Length <= _options.MaxDocumentLength)
            return document;

        var head = string.Concat(document.AsSpan(0, _options.MaxDocumentLength), TruncatedMarker);
        if (!context.TryGetOperation(out var operation))
            return head;

        return string.Concat(
            head,
            ExecutedFieldsMarker,
            string.Join(", ", ExecutedFieldCoordinates(operation)),
            "]"
        );
    }

    private const string TruncatedMarker = "...[truncated]";
    private const string ExecutedFieldsMarker = " [selected fields: ";

    /// <summary>
    /// Every <c>Type.field</c> the operation selects, across all of its possible types, in
    /// ordinal order. Walks each selection set once.
    /// </summary>
    private static SortedSet<string> ExecutedFieldCoordinates(Operation operation)
    {
        var coordinates = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<int>();
        var pending = new Stack<SelectionSet>();
        pending.Push(operation.RootSelectionSet);

        while (pending.Count > 0)
        {
            var selectionSet = pending.Pop();
            if (!visited.Add(selectionSet.Id))
                continue;

            foreach (var selection in selectionSet.Selections)
            {
                coordinates.Add($"{selection.DeclaringType.Name}.{selection.Field.Name}");
                if (selection.IsLeaf)
                    continue;
                foreach (var possibleType in operation.GetPossibleTypes(selection))
                    pending.Push(operation.GetSelectionSet(selection, possibleType));
            }
        }

        return coordinates;
    }

    private static IReadOnlyDictionary<string, object?>? BuildVariables(RequestContext context)
    {
        // VariableValues holds one collection per operation so batched requests keep their
        // values separate. The inner collection enumerates VariableValue directly.
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var collection in context.VariableValues)
        {
            if (collection is null)
                continue;
            foreach (var variable in collection)
                dict[variable.Name] = variable.Value?.ToString();
        }
        return dict.Count == 0 ? null : dict;
    }

    private IReadOnlyDictionary<string, object?>? SafeRedact(
        IReadOnlyDictionary<string, object?>? variables
    )
    {
        try
        {
            return redactor.Redact(variables);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit redactor threw. Dropping variables for safety.");
            return null;
        }
    }

    private static (bool Success, string? ErrorText) InterpretResult(RequestContext context)
    {
        if (
            context.ContextData.TryGetValue(ExceptionKey, out var parked)
            && parked is Exception exception
        )
            return (false, exception.Message);

        if (context.Result is OperationResult { Errors.Count: > 0 } operationResult)
        {
            var joined = string.Join("; ", operationResult.Errors.Select(e => e.Message));
            return (false, joined);
        }

        return (true, null);
    }

    private sealed class RequestScope(
        TraxGraphQLAuditListener listener,
        RequestContext context,
        long startTicks,
        DateTimeOffset startTime,
        (string Id, string? Type) principal
    ) : IDisposable
    {
        public void Dispose() => listener.CompleteScope(context, startTicks, startTime, principal);
    }
}
