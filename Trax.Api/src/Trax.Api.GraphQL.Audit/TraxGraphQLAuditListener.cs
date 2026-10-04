using System.Globalization;
using System.Text.Json.Nodes;
using HotChocolate;
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
            channel.RecordDropped(1, "Trax audit listener failed to start capturing a request.");
            return EmptyScope;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// HotChocolate 16 no longer hangs the request-level exception off the context, so the
    /// listener records it here and reads it back when the scope completes.
    /// </remarks>
    public override void RequestError(RequestContext context, Exception exception) =>
        context.ContextData[ExceptionKey] = exception;

    /// <summary>
    /// Both skips need the compiled operation, which exists only partway through the pipeline,
    /// so they are decided when the scope completes rather than when it opens. They apply only to
    /// a request that succeeded (for a subscription, one that returned a stream): a request that
    /// was refused or failed is always audited, because refusals are what an audit trail is for.
    /// A request that never produced an operation (a parse or validation failure) is audited too.
    /// See <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>.
    /// </summary>
    private bool ShouldSkipOnComplete(RequestContext context, bool success)
    {
        if (!success || (!_options.SkipIntrospection && !_options.SkipSubscriptions))
            return false;

        if (!context.TryGetOperation(out var operation))
            return false;

        if (_options.SkipSubscriptions && operation.Kind == OperationType.Subscription)
            return true;

        return _options.SkipIntrospection && SelectsOnlyIntrospection(operation);
    }

    /// <summary>
    /// True when every root selection of the compiled operation is <c>__schema</c>,
    /// <c>__type</c> or <c>__typename</c>. The compiled operation has its fragments expanded, so
    /// a field inside a top-level fragment counts as the field it is.
    /// </summary>
    private static bool SelectsOnlyIntrospection(Operation operation)
    {
        foreach (var selection in operation.RootSelectionSet.Selections)
            if (selection.Field.Name is not ("__schema" or "__type" or "__typename"))
                return false;

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
            var (success, errorText) = InterpretResult(context);
            if (ShouldSkipOnComplete(context, success))
                return;

            var elapsed = timeProvider.GetElapsedTime(startTicks);
            var document = CaptureDocument(context);
            var variables = BuildVariables(context);
            var redactedVariables = SafeRedact(variables);

            var entry = new TraxAuditEntry(
                PrincipalId: principal.Id,
                PrincipalType: principal.Type,
                OperationName: Truncate(
                    context.Request.OperationName,
                    _options.MaxOperationNameLength
                ),
                Document: document,
                Variables: redactedVariables,
                DurationMs: (long)elapsed.TotalMilliseconds,
                Timestamp: startTime,
                Success: success,
                ErrorText: Truncate(errorText, _options.MaxErrorTextLength),
                Metadata: null
            );

            channel.TryEnqueue(entry);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to build entry. Skipping.");
            channel.RecordDropped(1, "Trax audit listener failed to build an entry.");
        }
    }

    /// <summary>
    /// Returns the document with every string and numeric literal replaced by a placeholder
    /// (<see cref="AuditLiteralStripper"/>), so a value written inline, such as
    /// <c>login(password: "...")</c>, never reaches the sink. Past
    /// <see cref="TraxAuditOptions.MaxDocumentLength"/> it returns
    /// the head of that document followed by every field the compiled operation executes. The head alone can be
    /// filled by padding placed ahead of the fields that matter; the field list cannot, because
    /// it is read from the compiled operation after fragment expansion and holds each schema
    /// coordinate once, so its size is bounded by the schema rather than by the request.
    /// </summary>
    private string CaptureDocument(RequestContext context)
    {
        var parsed = RequestDocument(context);
        var document = parsed is null
            ? string.Empty
            : AuditLiteralStripper.Strip(parsed).ToString();
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

    /// <summary>
    /// The document the pipeline resolved, or, for a request refused before the pipeline looked
    /// it up (the endpoint policy refuses ahead of the document cache), the document the transport
    /// already parsed. A request that arrived as unparsed text and was refused that early has none.
    /// </summary>
    private static DocumentNode? RequestDocument(RequestContext context) =>
        context.OperationDocumentInfo.Document
        ?? (context.Request.Document as OperationDocument)?.Document;

    private const string TruncatedMarker = "...[truncated]";

    /// <summary>
    /// <paramref name="value"/> cut to <paramref name="maxLength"/> characters and marked, when
    /// it is longer.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), TruncatedMarker);

    private const string ExecutedFieldsMarker = " [selected fields: ";

    /// <summary>
    /// Every <c>Type.field</c> the operation selects, across all of its possible types, in
    /// ordinal order. The compiled operation is a tree, one selection set per selection and
    /// possible type, so each is reached once.
    /// </summary>
    private static SortedSet<string> ExecutedFieldCoordinates(Operation operation)
    {
        var coordinates = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<SelectionSet>();
        pending.Push(operation.RootSelectionSet);

        while (pending.Count > 0)
        {
            var selectionSet = pending.Pop();
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

    /// <summary>
    /// The request's variables as a JSON object the redactor can walk: an input object becomes a
    /// nested object and a list an array, so a field such as <c>$input.password</c> is reachable.
    /// Each call builds a new object, so the redactor may change it in place.
    /// </summary>
    private static JsonObject? BuildVariables(RequestContext context)
    {
        // VariableValues holds one collection per operation so batched requests keep their
        // values separate. The inner collection enumerates VariableValue directly.
        var variables = new JsonObject();
        foreach (var collection in context.VariableValues.OfType<IVariableValueCollection>())
        {
            foreach (var variable in collection)
                variables[variable.Name] = ToJson(variable.Value);
        }
        return variables.Count == 0 ? null : variables;
    }

    private static JsonNode? ToJson(IValueNode? value) =>
        value switch
        {
            null or NullValueNode => null,
            ObjectValueNode obj => new JsonObject(
                obj.Fields.Select(f => KeyValuePair.Create(f.Name.Value, ToJson(f.Value)))
            ),
            ListValueNode list => new JsonArray([.. list.Items.Select(ToJson)]),
            StringValueNode str => JsonValue.Create(str.Value),
            // The literal's own text, so a number keeps its exact value in the JSON.
            IntValueNode number => JsonNode.Parse(number.Value),
            FloatValueNode number => JsonNode.Parse(number.Value),
            BooleanValueNode boolean => JsonValue.Create(boolean.Value),
            // An enum value; any other node is recorded by its value's text the same way.
            _ => JsonValue.Create(Convert.ToString(value.Value, CultureInfo.InvariantCulture)),
        };

    private JsonObject? SafeRedact(JsonObject? variables)
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

    /// <summary>
    /// Whether the request failed, and what to record about why. By default an error is recorded
    /// as its code and path and an exception as its type, because a message can quote what the
    /// caller sent; <see cref="TraxAuditOptions.RecordErrorMessages"/> records the messages.
    /// </summary>
    private (bool Success, string? ErrorText) InterpretResult(RequestContext context)
    {
        if (
            context.ContextData.TryGetValue(ExceptionKey, out var parked)
            && parked is Exception exception
        )
            return (false, DescribeException(exception));

        if (context.Result is OperationResult { Errors.Count: > 0 } operationResult)
            return (false, DescribeErrors(operationResult.Errors));

        return (true, null);
    }

    private string DescribeErrors(IEnumerable<IError> errors) =>
        string.Join(
            "; ",
            errors.Select(e => _options.RecordErrorMessages ? e.Message : Describe(e))
        );

    /// <summary>
    /// A <see cref="GraphQLException"/> carries the errors it stands for, which are recorded like
    /// any other; any other exception is recorded as its type.
    /// </summary>
    private string DescribeException(Exception exception)
    {
        if (_options.RecordErrorMessages)
            return exception.Message;

        return exception is GraphQLException { Errors.Count: > 0 } graphQLException
            ? DescribeErrors(graphQLException.Errors)
            : exception.GetType().ToString();
    }

    /// <summary>What is recorded for an error by default: its code and where it was raised.</summary>
    private static string Describe(IError error)
    {
        var code = error.Code ?? MaskedErrorMarker;
        return error.Path is null ? code : $"{code} at {error.Path}";
    }

    private const string MaskedErrorMarker = "<masked>";

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
