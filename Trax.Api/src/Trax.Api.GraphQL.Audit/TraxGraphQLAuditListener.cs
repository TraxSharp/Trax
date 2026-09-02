using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
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
            if (ShouldSkipOnStart(context))
                return EmptyScope;

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
    /// Checks the only predicate that is knowable before the document is parsed: the
    /// caller-supplied operation name. The subscription check needs the compiled
    /// operation and therefore runs in <see cref="ShouldSkipOnComplete"/>.
    /// </summary>
    private bool ShouldSkipOnStart(RequestContext context)
    {
        if (!_options.SkipIntrospection)
            return false;

        var operationName = context.Request.OperationName;
        return string.Equals(operationName, "IntrospectionQuery", StringComparison.Ordinal);
    }

    /// <summary>
    /// The operation is only compiled partway through the pipeline, so its type cannot be
    /// read when the scope opens. Subscriptions are therefore filtered on the way out.
    /// </summary>
    private bool ShouldSkipOnComplete(RequestContext context) =>
        _options.SkipSubscriptions
        && context.TryGetOperation(out var operation)
        && operation.Kind == OperationType.Subscription;

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
            var document = TruncateDocument(
                context.OperationDocumentInfo.Document?.ToString() ?? string.Empty
            );
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

    private string TruncateDocument(string document)
    {
        if (document.Length <= _options.MaxDocumentLength)
            return document;

        return string.Concat(document.AsSpan(0, _options.MaxDocumentLength), "...[truncated]");
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
