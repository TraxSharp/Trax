using System.Globalization;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;
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

    /// <summary>
    /// Key under which <see cref="ExecuteRequest"/> keeps the caller it captured, so an error in a
    /// later event of an accepted subscription, which arrives after the request scope ended and
    /// outside the HTTP request, is recorded against the same caller.
    /// </summary>
    private const string PrincipalKey = "Trax.Audit.Principal";

    /// <summary>Metadata on an entry recording an error in one event of an accepted subscription.</summary>
    internal const string SubscriptionEventKey = "subscriptionEvent";

    private readonly TraxAuditOptions _options = options.Value;

    /// <inheritdoc />
    public override IDisposable ExecuteRequest(RequestContext context)
    {
        try
        {
            var startTicks = timeProvider.GetTimestamp();
            var startTime = timeProvider.GetUtcNow();
            var principal = CapturePrincipal();
            context.ContextData[PrincipalKey] = principal;

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
    /// Key under which <see cref="OnSubscriptionEvent"/> parks the collector for the event being
    /// executed. A subscription executes its events one at a time, so a request holds at most one.
    /// </summary>
    private const string EventErrorsKey = "Trax.Audit.EventErrors";

    /// <summary>Metadata on a failed event's entry: how many errors the event raised.</summary>
    internal const string SubscriptionEventErrorCountKey = "subscriptionEventErrors";

    /// <inheritdoc />
    /// <remarks>
    /// Opens a collector for one event of an accepted subscription. Every error the event raises,
    /// in any resolver or outside them, goes into it, and the event leaves exactly one entry when
    /// it completes, however many fields failed. See
    /// <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>.
    /// </remarks>
    public override IDisposable OnSubscriptionEvent(RequestContext context, ulong subscriptionId)
    {
        try
        {
            var errors = new EventErrors(_options.MaxErrorTextLength);
            context.ContextData[EventErrorsKey] = errors;
            return new EventScope(this, context, errors);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to start capturing an event.");
            return EmptyScope;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A resolver error inside an event of an accepted subscription. The subscribe step ran, and
    /// was audited or skipped, long before; each failing event is recorded,
    /// whatever <see cref="TraxAuditOptions.SkipSubscriptions"/> says, because only a successful
    /// request is skipped. The error joins the event's collector, so a failing event is one entry
    /// however many of its fields fail. A query's or mutation's resolver errors are already part
    /// of its request's entry. See <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>.
    /// </remarks>
    public override void ResolverError(IMiddlewareContext context, IError error)
    {
        try
        {
            if (context.Operation.Kind != OperationType.Subscription)
                return;

            RecordSubscriptionEventError(
                context.ContextData,
                context.Operation.Name,
                context.Operation.Document,
                _options.RecordErrorMessages ? error.Message : Describe(error)
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax audit listener failed to record a subscription event error."
            );
            channel.RecordDropped(
                1,
                "Trax audit listener failed to record a subscription event error."
            );
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// An accepted subscription's event failed outside any resolver, such as its source stream
    /// throwing. Recorded like a resolver error in an event: inside an event it joins that event's
    /// entry; a source stream that throws between events ends the subscription and leaves one
    /// entry of its own.
    /// </remarks>
    public override void SubscriptionEventError(
        RequestContext context,
        ulong subscriptionId,
        Exception exception
    )
    {
        try
        {
            context.TryGetOperation(out var operation);
            RecordSubscriptionEventError(
                context.ContextData,
                operation?.Name ?? context.Request.OperationName,
                RequestDocument(context),
                DescribeException(exception)
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax audit listener failed to record a subscription event error."
            );
            channel.RecordDropped(
                1,
                "Trax audit listener failed to record a subscription event error."
            );
        }
    }

    /// <summary>
    /// Adds the error to the collector of the event being executed, or, outside an event (the
    /// subscribe step, or a source stream that throws between events), records it at once.
    /// </summary>
    private void RecordSubscriptionEventError(
        IDictionary<string, object?> contextData,
        string? operationName,
        DocumentNode? document,
        string errorText
    )
    {
        if (contextData.TryGetValue(EventErrorsKey, out var open) && open is EventErrors errors)
        {
            errors.Add(errorText, operationName, document);
            return;
        }

        EnqueueSubscriptionEventError(contextData, operationName, document, errorText, 1);
    }

    private void CompleteEvent(RequestContext context, EventErrors errors)
    {
        try
        {
            context.ContextData.Remove(EventErrorsKey);
            if (errors.Count == 0)
                return;

            EnqueueSubscriptionEventError(
                context.ContextData,
                errors.OperationName,
                errors.Document,
                errors.Text,
                errors.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax audit listener failed to record a subscription event error."
            );
            channel.RecordDropped(
                1,
                "Trax audit listener failed to record a subscription event error."
            );
        }
    }

    private void EnqueueSubscriptionEventError(
        IDictionary<string, object?> contextData,
        string? operationName,
        DocumentNode? document,
        string errorText,
        int errorCount
    )
    {
        var principal =
            contextData.TryGetValue(PrincipalKey, out var captured)
            && captured is ValueTuple<string, string?> known
                ? known
                : (_options.DefaultPrincipalId, (string?)null);

        var text = document is null
            ? string.Empty
            : AuditLiteralStripper.Strip(document).ToString();

        channel.TryEnqueue(
            new TraxAuditEntry(
                PrincipalId: principal.Item1,
                PrincipalType: principal.Item2,
                OperationName: Truncate(operationName, _options.MaxOperationNameLength),
                Document: Truncate(text, _options.MaxDocumentLength)!,
                Variables: null,
                DurationMs: 0,
                Timestamp: timeProvider.GetUtcNow(),
                Success: false,
                ErrorText: Truncate(errorText, _options.MaxErrorTextLength),
                Metadata: new Dictionary<string, string>
                {
                    [SubscriptionEventKey] = "error",
                    [SubscriptionEventErrorCountKey] = errorCount.ToString(
                        CultureInfo.InvariantCulture
                    ),
                }
            )
        );
    }

    /// <summary>
    /// The errors one subscription event raised. Resolvers of one event can run in parallel, so it
    /// locks. It keeps only enough text to fill <see cref="TraxAuditOptions.MaxErrorTextLength"/>,
    /// and counts the rest, so an event with thousands of failing fields costs one bounded entry.
    /// </summary>
    private sealed class EventErrors(int maxTextLength)
    {
        private readonly Lock _gate = new();
        private readonly System.Text.StringBuilder _text = new();

        public int Count { get; private set; }

        public string? OperationName { get; private set; }

        public DocumentNode? Document { get; private set; }

        public string Text
        {
            get
            {
                lock (_gate)
                    return _text.ToString();
            }
        }

        public void Add(string errorText, string? operationName, DocumentNode? document)
        {
            lock (_gate)
            {
                Count++;
                OperationName ??= operationName;
                Document ??= document;
                // One character past the cap is enough for the entry to be marked truncated.
                if (_text.Length > maxTextLength)
                    return;
                if (_text.Length > 0)
                    _text.Append("; ");
                _text.Append(errorText);
            }
        }
    }

    private sealed class EventScope(
        TraxGraphQLAuditListener listener,
        RequestContext context,
        EventErrors errors
    ) : IDisposable
    {
        public void Dispose() => listener.CompleteEvent(context, errors);
    }

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
                OperationName: Truncate(OperationName(context), _options.MaxOperationNameLength),
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

    /// <summary>
    /// The operation name the request sent, or, when it sent none, the name the document gives
    /// its only operation. A client that names an operation only in the document (<c>query
    /// WhoAmI { ... }</c> with no <c>operationName</c> field) is recorded under that name. A
    /// document with several operations needs the request field to pick one, so without it
    /// the name stays empty.
    /// </summary>
    private static string? OperationName(RequestContext context)
    {
        if (context.Request.OperationName is { } requested)
            return requested;

        OperationDefinitionNode? only = null;
        foreach (var definition in RequestDocument(context)?.Definitions ?? [])
        {
            if (definition is not OperationDefinitionNode operation)
                continue;
            if (only is not null)
                return null;
            only = operation;
        }

        return only?.Name?.Value;
    }

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
