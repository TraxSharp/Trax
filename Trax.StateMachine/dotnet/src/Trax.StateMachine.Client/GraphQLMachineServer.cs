using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Client;

namespace Trax.StateMachine.Client;

/// <summary>How a <see cref="GraphQLMachineServer"/> reaches its host.</summary>
public sealed record GraphQLMachineServerOptions
{
    /// <summary>
    /// The client the requests go through. Set its authentication on it, a default header or a delegating handler, as
    /// for any other <see cref="System.Net.Http.HttpClient"/>. Build it on a handler with automatic redirects off: a
    /// redirect drops only <c>Authorization</c>, so the body and any other header would follow it to the new host. A
    /// client on an <see cref="HttpClientHandler"/> or <see cref="SocketsHttpHandler"/> that follows redirects,
    /// <c>new HttpClient()</c> among them, is refused when the server is built. An answer from another host than
    /// <see cref="Endpoint"/>'s, through a handler of another kind that followed one, fails the request.
    /// </summary>
    public required HttpClient Http { get; init; }

    /// <summary>
    /// The host's GraphQL endpoint (<c>https://host/trax/graphql</c>), or one relative to <see cref="Http"/>'s base
    /// address. It must be <c>https</c> unless its host is loopback or <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Accepts an <c>http</c> endpoint on a host that is not loopback. Off by default: whatever authenticates the
    /// requests would travel in cleartext.
    /// </summary>
    public bool AllowInsecureTransport { get; init; }

    /// <summary>
    /// The machine as the host's client manifest lists it, with its persisted documents, the push (<c>changed</c>)
    /// included. Every operation is sent by its document's id; a client never sends a document of its own.
    /// </summary>
    public required ClientManifestMachine Machine { get; init; }

    /// <summary>The socket the change stream is subscribed over.</summary>
    public required ISubscriptionSocket Socket { get; init; }

    /// <summary>
    /// Delays before subscribing again after a stream that ended without saying why (its host stopped, or the socket
    /// closed). The last repeats for as long as the stream is wanted; they start over once a stream has delivered a
    /// second push, since every stream's first is the instance as it is. A socket the host refused (4401, 4403, or an
    /// upgrade answered 401 or 403), or a stream it refused with a GraphQL error coded <c>TRAX_AUTHORIZATION</c>,
    /// <c>AUTH_NOT_AUTHENTICATED</c> or <c>AUTH_NOT_AUTHORIZED</c>, is not subscribed again: it stops the controller as
    /// <c>UNAUTHENTICATED</c> or <c>FORBIDDEN</c>.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResubscribeDelays { get; init; }

    /// <summary>
    /// The codes of a stream error that ends the controller for good, beside the refusals above. A stream event or error
    /// whose first GraphQL error has one as its <c>extensions.code</c> is not subscribed again: it reaches the
    /// controller as <see cref="StreamMessage.Refused"/> with that code and the error's message.
    /// </summary>
    public IReadOnlyCollection<string>? TerminalStreamErrors { get; init; }

    /// <summary>
    /// The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one fails the request,
    /// which the controller reports as a transport failure, rather than being read into memory.
    /// </summary>
    public long MaxResponseSize { get; init; } = 16 * 1024 * 1024;
}

/// <summary>
/// The <see cref="IMachineServer"/> over a host's typed <c>machines</c> root: each operation is a persisted document of
/// the host's client manifest, sent by its id with its values as variables, and the change stream is the machine's
/// <c>&lt;machine&gt;Changed</c> subscription, subscribed by its id over an <see cref="ISubscriptionSocket"/>.
/// </summary>
/// <remarks>
/// A refusal is an answer, and so is <c>CLIENT_OUTDATED</c> or <c>SERVER_OUTDATED</c>, the host's answer to a document of
/// another version of the machine. Any other GraphQL error, an HTTP status other than success, or a request that does
/// not complete, throws: the controller reports it as a transport failure.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/dotnet-client">SDK reference</seealso>
public sealed partial class GraphQLMachineServer : IMachineServer
{
    /// <summary>The default delays before subscribing again after a stream ended without a reason.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultResubscribeDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    private const string ChangedOperation = "changed";
    private static readonly string ClientOutdated = RefusalCode.ClientOutdated.ToWire();
    private static readonly string ServerOutdated = RefusalCode.ServerOutdated.ToWire();

    // The GraphQL error codes a host refuses one subscription with on a socket it accepted (an expired token, an
    // anonymous socket), and the code each stops the controller with. Subscribing again would get the same answer.
    private static readonly IReadOnlyDictionary<string, string> RefusedErrors = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["TRAX_AUTHORIZATION"] = RefusalCode.Forbidden.ToWire(),
        ["AUTH_NOT_AUTHENTICATED"] = RefusalCode.Unauthenticated.ToWire(),
        ["AUTH_NOT_AUTHORIZED"] = RefusalCode.Forbidden.ToWire(),
    };

    private readonly GraphQLMachineServerOptions _options;
    private readonly MachineShape _shape;
    private readonly string _field;
    private readonly string _pushField;
    private readonly IReadOnlyDictionary<string, Document> _documents;
    private readonly Document _changed;
    private readonly IReadOnlyList<TimeSpan> _resubscribeDelays;
    private readonly IReadOnlySet<string> _terminal;

    private sealed record Document(string Id, IReadOnlySet<string> Variables);

    /// <summary>Builds the server for one machine.</summary>
    /// <exception cref="ArgumentException">
    /// The machine lists no documents, or no <c>changed</c> document, or the endpoint is relative and the client has no
    /// base address, or it is not <c>https</c> on a host that is not loopback and
    /// <see cref="GraphQLMachineServerOptions.AllowInsecureTransport"/> is not set, or the client follows redirects.
    /// </exception>
    public GraphQLMachineServer(GraphQLMachineServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TransportSecurity.RequireNoRedirects(options.Http, nameof(options));
        TransportSecurity.RequireSecure(
            options.Endpoint.IsAbsoluteUri || options.Http?.BaseAddress is null
                ? options.Endpoint
                : new Uri(options.Http!.BaseAddress, options.Endpoint),
            options.AllowInsecureTransport,
            Uri.UriSchemeHttps,
            nameof(options)
        );
        _options = options;
        _shape = MachineShape.Of(options.Machine);
        _field = MachineField(_shape.Id);
        _pushField = _field + "Changed";
        if (options.Machine.Documents.Count == 0)
            throw new ArgumentException(
                $"The machine '{_shape.Id}' has no persisted documents. Export the host's client manifest "
                    + "(--trax-export-client) and pass its machine: a client sends only the ids the host registered.",
                nameof(options)
            );
        _documents = options.Machine.Documents.ToDictionary(
            d => d.Operation,
            d => new Document(
                d.Id,
                DeclaredVariable()
                    .Matches(d.Document)
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal)
            ),
            StringComparer.Ordinal
        );
        _changed =
            _documents.GetValueOrDefault(ChangedOperation)
            ?? throw new ArgumentException(
                $"The documents of '{_shape.Id}' have no {ChangedOperation} document. Export them again from the host's manifest.",
                nameof(options)
            );
        _resubscribeDelays = options.ResubscribeDelays is { Count: > 0 } delays
            ? delays
            : DefaultResubscribeDelays;
        _terminal = (options.TerminalStreamErrors ?? []).ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public Task<ServerAnswer> StartAsync(
        JsonNode? input,
        string requestId,
        CancellationToken cancellationToken = default
    ) =>
        RunAsync(
            "start",
            new JsonObject { ["input"] = input, ["requestId"] = requestId },
            cancellationToken
        );

    /// <inheritdoc/>
    /// <remarks>
    /// A trigger the machine does not declare as an event is refused without a request, with the code the engine's
    /// advance refuses it with (<c>NO_TRANSITION</c>, <c>OUTCOME_BOUND</c> or <c>TIMER_BOUND</c>), and so is one whose
    /// field is named like one of the machine's own operations (<c>start</c>, <c>get</c>, <c>discard</c>,
    /// <c>changed</c>), which no host serves as an event (<c>NO_TRANSITION</c>).
    /// </remarks>
    public Task<ServerAnswer> EventAsync(
        string id,
        string trigger,
        JsonNode? input,
        string requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    )
    {
        if (_shape.Undeclared(trigger) is (var code, var message))
            return Task.FromResult(ServerAnswer.Refused(code.ToWire(), message));
        var field = EventField(trigger);
        if (MachineShape.Operations.Contains(field))
            return Task.FromResult(
                ServerAnswer.Refused(
                    RefusalCode.NoTransition.ToWire(),
                    $"'{trigger}' is named like the machine's {field} operation, which is never sent as an event."
                )
            );
        return RunAsync(
            field,
            new JsonObject
            {
                ["id"] = id,
                ["input"] = input,
                ["requestId"] = requestId,
                ["clientResult"] = clientResult,
            },
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public async Task<ServerAnswer> GetAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        var answer = await RunAsync("get", new JsonObject { ["id"] = id }, cancellationToken)
            .ConfigureAwait(false);
        return answer.Problem is null && answer.Snapshot is null
            ? ServerAnswer.Refused(RefusalCode.NotFound.ToWire(), "No instance with that id.")
            : answer;
    }

    /// <summary>
    /// Deletes one of the caller's instances. An instance already gone, or never the caller's, is answered discarded too,
    /// so a retry is harmless. Null when it was discarded; otherwise the refusal. An answer that neither says the
    /// instance was discarded nor gives a problem does not say it is gone, and throws as a failed request does.
    /// </summary>
    public async Task<ServerProblem?> DiscardAsync(
        string id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        var (answer, refused) = await PostAsync(
                "discard",
                new JsonObject { ["id"] = id, ["requestId"] = requestId },
                cancellationToken
            )
            .ConfigureAwait(false);
        if (refused is not null)
            return refused.Problem;
        if (answer?["problem"] is JsonObject problem)
            return ProblemOf(problem);
        if (
            answer?["discarded"] is not JsonValue discarded
            || !discarded.TryGetValue(out bool gone)
            || !gone
        )
            throw new HttpRequestException(
                "The server answered the discard without saying the instance was discarded."
            );
        return null;
    }

    // One operation, by its persisted id, its values as variables.
    private async Task<ServerAnswer> RunAsync(
        string operation,
        JsonObject values,
        CancellationToken cancellationToken
    )
    {
        var (answer, refused) = await PostAsync(operation, values, cancellationToken)
            .ConfigureAwait(false);
        return refused ?? AnswerOf(answer);
    }

    private async Task<(JsonObject? Answer, ServerAnswer? Refused)> PostAsync(
        string operation,
        JsonObject values,
        CancellationToken cancellationToken
    )
    {
        if (!_documents.TryGetValue(operation, out var document))
            return (
                null,
                ServerAnswer.Refused(
                    ClientOutdated,
                    $"There is no document for '{_shape.Id}.{operation}': the documents are of another version of the machine."
                )
            );

        var variables = new JsonObject();
        foreach (var (name, value) in values)
            if (value is not null && document.Variables.Contains(name))
                variables[name] = value.DeepClone();
        var body = new JsonObject { ["id"] = document.Id, ["variables"] = variables };

        var answered = await TraxHttpTransport
            .PostAsync(
                _options.Http,
                _options.Endpoint,
                body,
                _options.MaxResponseSize,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (answered.Failure is { } failure)
            throw new HttpRequestException(
                failure.Message,
                null,
                failure.Status is { } status ? (System.Net.HttpStatusCode)status : null
            );
        // A GraphQL error with a code is the host's to answer with whatever status it chose.
        if (answered.Errors.Count > 0)
        {
            var error = answered.Errors[0];
            if (error.Code == ClientOutdated || error.Code == ServerOutdated)
                return (null, ServerAnswer.Refused(error.Code, error.Message));
            throw new HttpRequestException(error.Message);
        }

        return (answered.Data?["machines"]?[_field]?[operation] as JsonObject, null);
    }

    private ServerAnswer AnswerOf(JsonObject? answer)
    {
        if (answer is null)
            return new ServerAnswer();
        var id = Text(answer["id"]);
        if (answer["problem"] is JsonObject problem)
            return new ServerAnswer { Id = id, Problem = ProblemOf(problem) };
        return ServerAnswer.Of(
            new ServerSnapshot
            {
                // A revision the store does not record stays null: it is not revision 0, which would order every
                // later answer below it.
                Revision = answer["revision"] is JsonValue revision
                    ? revision.GetValue<long>()
                    : null,
                State =
                    Text(answer["state"])
                    ?? throw new HttpRequestException(
                        "The server answered a snapshot without a state."
                    ),
                Context = WithoutNulls(answer["context"] as JsonObject),
                // A due time the server says it cannot know is left unknown, so it is never read as "no timer".
                DueAtKnown = answer["dueAtKnown"] is not JsonValue known || known.GetValue<bool>(),
                DueAt = DueAtOf(answer["dueAt"]),
            },
            id
        );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each push becomes a message: a snapshot (with its incarnation), <see cref="StreamMessage.Gone"/> for a deleted
    /// instance or one the server will not let the caller watch, <see cref="StreamMessage.ClientOutdated"/>, and
    /// <see cref="StreamMessage.Reload"/> when the server asks for one. A socket the host refused (closed with 4401 or
    /// 4403, or an upgrade answered 401 or 403) is <see cref="StreamMessage.Refused"/>, and is not subscribed again; so
    /// is a stream the host refused with a GraphQL error coded <c>TRAX_AUTHORIZATION</c> or <c>AUTH_NOT_AUTHORIZED</c>
    /// (<c>FORBIDDEN</c>) or <c>AUTH_NOT_AUTHENTICATED</c> (<c>UNAUTHENTICATED</c>), one whose error code is among
    /// <see cref="GraphQLMachineServerOptions.TerminalStreamErrors"/> (with that code), and a push the client cannot
    /// read (a member of the wrong type, or one given twice), as <see cref="ClientProblemCodes.TransportError"/>. A
    /// stream that ends any other way is subscribed again after the resubscribe delays; the new stream's first event
    /// is the instance as it is then.
    /// </remarks>
    public IDisposable Subscribe(string id, Action<StreamMessage> onMessage)
    {
        var subscription = new Resubscribing(this, id, onMessage);
        subscription.Open();
        return subscription;
    }

    private sealed class Resubscribing(
        GraphQLMachineServer server,
        string id,
        Action<StreamMessage> onMessage
    ) : IDisposable, ISubscriptionSink
    {
        private readonly object _gate = new();
        private bool _wanted = true;
        private int _attempt;

        // The pushes the current stream delivered. Every stream's first push is the instance as it is, so only a second
        // says the stream held: the resubscribe delays start over then.
        private int _pushes;
        private bool _ended;
        private IDisposable? _current;
        private CancellationTokenSource? _timer;

        public void Open()
        {
            lock (_gate)
            {
                _timer = null;
                if (!_wanted)
                    return;
                _ended = false;
                _pushes = 0;
            }
            var current = server._options.Socket.Subscribe(
                new JsonObject
                {
                    ["id"] = server._changed.Id,
                    ["variables"] = new JsonObject { ["id"] = id },
                },
                this
            );
            bool close;
            lock (_gate)
            {
                close = !_wanted || _ended;
                if (!close)
                    _current = current;
            }
            if (close)
                current.Dispose();
        }

        public void Next(JsonObject value)
        {
            StreamMessage? message;
            bool again;
            try
            {
                (message, again) = Read(value);
            }
            catch (Exception ex)
            {
                // A member of the wrong type, or one given twice: the host would send the same push again, so the
                // stream ends once rather than being subscribed again.
                Deliver(
                    new StreamMessage.Refused(
                        ClientProblemCodes.TransportError,
                        $"The host sent a push this client cannot read: {ex.Message}"
                    )
                );
                return;
            }
            if (again)
                Again();
            else if (message is not null)
            {
                lock (_gate)
                    if (++_pushes == 2)
                        _attempt = 0;
                Deliver(message);
            }
        }

        // What one push says: a message to deliver, a stream to subscribe again, or nothing. Throws on a push it cannot
        // read.
        private (StreamMessage? Message, bool Again) Read(JsonObject value)
        {
            if (value["errors"] is JsonArray { Count: > 0 } errors)
            {
                var ended = EndOf(errors);
                return (ended, ended is null);
            }
            if (value["data"]?[server._pushField] is not JsonObject change)
                return (null, false);
            if (change["problem"] is JsonObject problem)
            {
                var code = Text(problem["code"]);
                if (code == ClientOutdated)
                    return (new StreamMessage.ClientOutdated(), false);
                if (code == RefusalCode.NotFound.ToWire())
                    return (new StreamMessage.Gone(NotFound: true), false);
                return (null, true);
            }
            if (change["gone"] is JsonValue gone && gone.GetValue<bool>())
                return (new StreamMessage.Gone(NotFound: false), false);
            if (change["reload"] is JsonValue reload && reload.GetValue<bool>())
                return (new StreamMessage.Reload(), false);
            if (
                Text(change["state"]) is not { } state
                || change["revision"] is not JsonValue revision
            )
                return (null, false);
            return (
                new StreamMessage.Changed(
                    new ServerSnapshot
                    {
                        RowId = change["rowId"] is JsonValue row
                            ? row.ToJsonString().Trim('"')
                            : null,
                        Revision = revision.GetValue<long>(),
                        State = state,
                        Context = WithoutNulls(change["context"] as JsonObject),
                        DueAtKnown =
                            change["dueAtKnown"] is not JsonValue known || known.GetValue<bool>(),
                        DueAt = DueAtOf(change["dueAt"]),
                    }
                ),
                false
            );
        }

        public void Error(Exception error)
        {
            if (RefusedCode(error) is { } code)
                Deliver(new StreamMessage.Refused(code, error.Message));
            else if (
                error is SubscriptionErrorException { Errors: var errors }
                && EndOf(errors) is { } ended
            )
                Deliver(ended);
            else
                Again();
        }

        // How GraphQL errors end the stream for good: CLIENT_OUTDATED, a refusal the host answers per operation, or a
        // code the caller named terminal. Null for any other, which is subscribed again.
        private StreamMessage? EndOf(JsonArray errors)
        {
            if (
                errors.Count == 0
                || errors[0] is not JsonObject first
                || Text((first["extensions"] as JsonObject)?["code"]) is not { } code
            )
                return null;
            if (code == ClientOutdated)
                return new StreamMessage.ClientOutdated();
            var message = Text(first["message"]) is { Length: > 0 } said ? said : code;
            if (RefusedErrors.TryGetValue(code, out var refused))
                return new StreamMessage.Refused(
                    refused,
                    $"The host refused the stream: {message}"
                );
            return server._terminal.Contains(code)
                ? new StreamMessage.Refused(code, message)
                : null;
        }

        // The code a socket the host refused stops the controller with, or null for a close to subscribe again after.
        private static string? RefusedCode(Exception error) =>
            error switch
            {
                SocketClosedException { CloseStatus: 4401 }
                or SocketClosedException { HttpStatus: 401 } =>
                    RefusalCode.Unauthenticated.ToWire(),
                SocketClosedException { CloseStatus: 4403 }
                or SocketClosedException { HttpStatus: 403 } => RefusalCode.Forbidden.ToWire(),
                _ => null,
            };

        public void Complete() => Again();

        private void Deliver(StreamMessage message)
        {
            lock (_gate)
            {
                if (!_wanted || _ended)
                    return;
                if (
                    message
                    is StreamMessage.Gone
                        or StreamMessage.ClientOutdated
                        or StreamMessage.Refused
                )
                    _ended = true;
            }
            onMessage(message);
        }

        // The stream ended without a reason: close it and subscribe again after the next delay.
        private void Again()
        {
            IDisposable? close;
            TimeSpan delay;
            CancellationTokenSource timer;
            lock (_gate)
            {
                if (!_wanted || _ended)
                    return;
                _ended = true;
                close = _current;
                _current = null;
                var delays = server._resubscribeDelays;
                delay = delays[Math.Min(_attempt, delays.Count - 1)];
                _attempt++;
                timer = _timer = new CancellationTokenSource();
            }
            close?.Dispose();
            _ = Task.Delay(delay, timer.Token)
                .ContinueWith(
                    t =>
                    {
                        if (!t.IsCanceled)
                            Open();
                    },
                    TaskScheduler.Default
                );
        }

        public void Dispose()
        {
            IDisposable? close;
            CancellationTokenSource? timer;
            lock (_gate)
            {
                _wanted = false;
                close = _current;
                _current = null;
                timer = _timer;
                _timer = null;
            }
            timer?.Cancel();
            close?.Dispose();
        }
    }

    private static ServerProblem ProblemOf(JsonObject problem) =>
        new(Text(problem["code"]) ?? "", Text(problem["message"]) ?? "");

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateTimeOffset? DueAtOf(JsonNode? node) =>
        Text(node) is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

    /// <summary>
    /// The context with every null member left out, at every depth: a typed answer cannot tell a member stored as null
    /// from one the state does not hold, and the server compares a prediction with null and absent equal.
    /// </summary>
    internal static JsonObject WithoutNulls(JsonObject? context)
    {
        var result = new JsonObject();
        if (context is null)
            return result;
        foreach (var (name, value) in context)
            if (value is not null)
                result[name] = Strip(value);
        return result;
    }

    private static JsonNode Strip(JsonNode node) =>
        node switch
        {
            JsonObject o => WithoutNulls(o),
            JsonArray a => new JsonArray(a.Select(v => v is null ? null : Strip(v)).ToArray()),
            _ => node.DeepClone(),
        };

    /// <summary>The machine's field under <c>machines</c>: its kebab-case id in camelCase (<c>topic-map</c> is <c>topicMap</c>).</summary>
    internal static string MachineField(string machineId) =>
        string.Concat(
            machineId
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select((part, i) => i == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..])
        );

    /// <summary>
    /// An event's field: the trigger in camelCase, as the server names it. The leading run of capitals is lowered, keeping
    /// the last one of a run a lowercase letter follows (<c>ID</c> is <c>id</c>, <c>URLPath</c> is <c>urlPath</c>).
    /// </summary>
    internal static string EventField(string trigger)
    {
        var upper = 0;
        while (upper < trigger.Length && trigger[upper] is >= 'A' and <= 'Z')
            upper++;
        if (upper == 0)
            return trigger;
        if (upper == 1 || upper == trigger.Length)
            return trigger[..upper].ToLowerInvariant() + trigger[upper..];
        return trigger[..(upper - 1)].ToLowerInvariant() + trigger[(upper - 1)..];
    }

    [GeneratedRegex(@"\$(\w+)\s*:")]
    private static partial Regex DeclaredVariable();
}
