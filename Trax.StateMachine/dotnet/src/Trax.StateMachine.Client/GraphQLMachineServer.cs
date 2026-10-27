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
    /// redirect drops only <c>Authorization</c>, so any other header would follow it to the new host. An answer from
    /// another host than <see cref="Endpoint"/>'s fails the request.
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
    /// closed). The last repeats for as long as the stream is wanted. A socket the host refused (4401, 4403, or an
    /// upgrade answered 401 or 403) is not subscribed again: it stops the controller.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResubscribeDelays { get; init; }

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

    private readonly GraphQLMachineServerOptions _options;
    private readonly MachineShape _shape;
    private readonly string _field;
    private readonly string _pushField;
    private readonly IReadOnlyDictionary<string, Document> _documents;
    private readonly Document _changed;
    private readonly IReadOnlyList<TimeSpan> _resubscribeDelays;

    private sealed record Document(string Id, IReadOnlySet<string> Variables);

    /// <summary>Builds the server for one machine.</summary>
    /// <exception cref="ArgumentException">
    /// The machine lists no documents, or no <c>changed</c> document, or the endpoint is not <c>https</c> on a host that
    /// is not loopback and <see cref="GraphQLMachineServerOptions.AllowInsecureTransport"/> is not set.
    /// </exception>
    public GraphQLMachineServer(GraphQLMachineServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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
    public Task<ServerAnswer> EventAsync(
        string id,
        string trigger,
        JsonNode? input,
        string requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    ) =>
        RunAsync(
            EventField(trigger),
            new JsonObject
            {
                ["id"] = id,
                ["input"] = input,
                ["requestId"] = requestId,
                ["clientResult"] = clientResult,
            },
            cancellationToken
        );

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
    /// so a retry is harmless. Null when it was discarded; otherwise the refusal.
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
        return answer?["problem"] is JsonObject problem ? ProblemOf(problem) : null;
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
    /// 4403, or an upgrade answered 401 or 403) is <see cref="StreamMessage.Refused"/>, and is not subscribed again. A
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
            if (value["errors"] is JsonArray { Count: > 0 } errors)
            {
                if (
                    errors[0]?["extensions"]?["code"] is JsonValue c
                    && c.TryGetValue<string>(out var code)
                    && code == ClientOutdated
                )
                    Deliver(new StreamMessage.ClientOutdated());
                else
                    Again();
                return;
            }
            if (value["data"]?[server._pushField] is not JsonObject change)
                return;
            if (change["problem"] is JsonObject problem)
            {
                var code = Text(problem["code"]);
                if (code == ClientOutdated)
                    Deliver(new StreamMessage.ClientOutdated());
                else if (code == RefusalCode.NotFound.ToWire())
                    Deliver(new StreamMessage.Gone(NotFound: true));
                else
                    Again();
                return;
            }
            if (change["gone"] is JsonValue gone && gone.GetValue<bool>())
            {
                Deliver(new StreamMessage.Gone(NotFound: false));
                return;
            }
            if (change["reload"] is JsonValue reload && reload.GetValue<bool>())
            {
                Deliver(new StreamMessage.Reload());
                return;
            }
            if (
                Text(change["state"]) is not { } state
                || change["revision"] is not JsonValue revision
            )
                return;
            lock (_gate)
                _attempt = 0;
            Deliver(
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
                )
            );
        }

        public void Error(Exception error)
        {
            if (RefusedCode(error) is { } code)
                Deliver(new StreamMessage.Refused(code, error.Message));
            else
                Again();
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
