using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Client;
using Trax.Core.Extensions;
using Trax.StateMachine.Client;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// The C# client as a client-conformance testee (<c>Trax.StateMachine/conformance/client/PROTOCOL.md</c>): a
/// <see cref="ServerMachineController"/> over a <see cref="GraphQLMachineServer"/> and a
/// <see cref="GraphQLSubscriptionSocket"/>, predicting with the machine built from the IR the transcript gives, as an app
/// that holds only the host's manifest does, and driven through the package's public API only.
/// </summary>
public sealed class ClientTestee : IAsyncDisposable
{
    // The name the kit's allowlist of what a client may lack knows this client by.
    internal const string Client = "dotnet";

    private HttpClient _http;
    private ServerMachineController? _controller;
    private GraphQLMachineServer? _server;
    private Func<string> _newRequestId = () => "r0";
    private GraphQLSubscriptionSocket? _socket;

    // The open request the client was built from, which a build overrides, and whether the headers its requests are
    // given throw (PROTOCOL.md, headers).
    private JsonObject? _opened;
    private volatile bool _headersThrow;

    public ClientTestee() => _http = NewHttp();

    /// <summary>Answers one request line with one answer line, once the request has settled.</summary>
    public async Task<string> AnswerAsync(string line)
    {
        var request = (JsonObject)JsonNode.Parse(line).NotNull();
        JsonObject answer;
        try
        {
            answer = await HandleAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            answer = new JsonObject { ["error"] = $"threw: {ex.Message}" };
        }
        answer["id"] = request["id"]?.DeepClone();
        return answer.ToJsonString(Writing);
    }

    // A view holds a context as deep as a stored snapshot may nest (64 levels) inside the answer around it, past
    // System.Text.Json's default writing depth of 64.
    private static readonly System.Text.Json.JsonSerializerOptions Writing = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        MaxDepth = 256,
    };

    private async Task<JsonObject> HandleAsync(JsonObject request)
    {
        var op = request["op"].NotNull().GetValue<string>();
        switch (op)
        {
            case "open":
                await CloseAsync().ConfigureAwait(false);
                return Open(request);
            case "dispose":
                await CloseAsync().ConfigureAwait(false);
                return new JsonObject { ["disposed"] = true };
            case "end":
                await CloseAsync().ConfigureAwait(false);
                return new JsonObject();
        }
        if (_controller is not { } controller)
            return new JsonObject { ["error"] = "no client is open" };
        return op switch
        {
            "start" => await Settled(controller.StartAsync()).ConfigureAwait(false),
            "send" => await AnswerSendAsync(controller, request).ConfigureAwait(false),
            "build" => await AnswerBuildAsync(request).ConfigureAwait(false),
            "headers" => Headers(request),
            "retry" => OutcomeOf(await controller.RetryAsync().ConfigureAwait(false)),
            "sync" => await AnswerSyncAsync(controller, request).ConfigureAwait(false),
            "view" => ViewOf(controller.View),
            "can" => new JsonObject
            {
                ["can"] = controller.CanSend(
                    request["trigger"].NotNull().GetValue<string>(),
                    request["input"]?.DeepClone()
                ),
            },
            "available" => new JsonObject
            {
                ["triggers"] = new JsonArray(
                    controller
                        .AvailableTriggers()
                        .Order(StringComparer.Ordinal)
                        .Select(t => (JsonNode?)JsonValue.Create(t))
                        .ToArray()
                ),
            },
            "discard" => await DiscardAsync(controller.View.Id).ConfigureAwait(false),
            _ => new JsonObject { ["unsupported"] = true },
        };
    }

    private static async Task<JsonObject> AnswerSendAsync(
        ServerMachineController controller,
        JsonObject request
    ) =>
        OutcomeOf(
            await controller
                .SendAsync(
                    request["trigger"].NotNull().GetValue<string>(),
                    string.Equals(
                        request["tooDeep"]?.GetValue<string>(),
                        "input",
                        StringComparison.Ordinal
                    )
                        ? TooDeep()
                        : request["input"]?.DeepClone()
                )
                .ConfigureAwait(false)
        );

    private static async Task<JsonObject> AnswerSyncAsync(
        ServerMachineController controller,
        JsonObject request
    ) =>
        SyncOf(
            await controller
                .SyncToAsync(
                    request["target"].NotNull().GetValue<string>(),
                    request["fields"] is JsonObject fields
                        ? new PlannedEvent(
                            fields["trigger"].NotNull().GetValue<string>(),
                            string.Equals(
                                request["tooDeep"]?.GetValue<string>(),
                                "input",
                                StringComparison.Ordinal
                            )
                                ? TooDeep()
                                : fields["input"]?.DeepClone()
                        )
                        : null,
                    request["navigation"]
                        .NotNull()
                        .AsArray()
                        .Select(t => t.NotNull().GetValue<string>())
                        .ToList()
                )
                .ConfigureAwait(false)
        );

    // The controller has no discard of its own: an app discards through the server it built, under a request id it
    // mints. The discard is total: one that throws is not an outcome, and fails the transcript as the testee's error.
    private async Task<JsonObject> DiscardAsync(string? id)
    {
        if (id is null || _server is not { } server)
            return new JsonObject { ["error"] = "no instance to discard" };
        var problem = await server.DiscardAsync(id, _newRequestId()).ConfigureAwait(false);
        if (problem is null)
            return new JsonObject { ["outcome"] = "discarded" };
        return new JsonObject
        {
            ["outcome"] = string.Equals(
                problem.Code,
                ClientProblemCodes.TransportError,
                StringComparison.Ordinal
            )
                ? "failed"
                : "refused",
            ["code"] = problem.Code,
        };
    }

    // 600 arrays nested one in another, built in memory (PROTOCOL.md, sync, tooDeep): no reader takes the text.
    private static JsonNode TooDeep()
    {
        var deep = new JsonArray();
        for (var i = 1; i < 600; i++)
            deep = new JsonArray(deep);
        return deep;
    }

    private static async Task<JsonObject> Settled(Task started)
    {
        await started.ConfigureAwait(false);
        return new JsonObject { ["settled"] = true };
    }

    private JsonObject Open(JsonObject request)
    {
        _headersThrow = false;
        _http.Dispose();
        _http = NewHttp();
        var built = Build(request);
        if (built.Code is { } code)
            return new JsonObject
            {
                ["opened"] = false,
                ["client"] = Client,
                ["code"] = code,
            };
        (_socket, _server, _controller, _newRequestId) = (
            built.Socket,
            built.Server,
            built.Controller,
            built.NewRequestId.NotNull()
        );
        _opened = request;
        if (request["throwingListener"]?.GetValue<bool>() == true)
            _controller.NotNull().Changed += (_, _) =>
                throw new InvalidOperationException("the change listener threw");
        return new JsonObject { ["opened"] = true, ["client"] = Client };
    }

    // Builds a client from the open request with the build's members over it, reports whether it was built, and
    // disposes it unstarted.
    private async Task<JsonObject> AnswerBuildAsync(JsonObject request)
    {
        var merged = (JsonObject)_opened.NotNull().DeepClone();
        foreach (var (key, value) in request)
            if (key is not "id" and not "op")
                merged[key] = value?.DeepClone();
        var built = Build(merged);
        if (built.Code is { } code)
            return new JsonObject { ["built"] = false, ["code"] = code };
        built.Controller.NotNull().Dispose();
        await built.Socket.NotNull().DisposeAsync().ConfigureAwait(false);
        return new JsonObject { ["built"] = true };
    }

    private JsonObject Headers(JsonObject request)
    {
        _headersThrow = request["throws"]?.GetValue<bool>() == true;
        return new JsonObject { ["throws"] = _headersThrow };
    }

    private sealed record Built(
        GraphQLSubscriptionSocket? Socket,
        GraphQLMachineServer? Server,
        ServerMachineController? Controller,
        Func<string>? NewRequestId,
        string? Code
    );

    private Built Build(JsonObject request)
    {
        var name = request["machine"].NotNull().GetValue<string>();
        var machine = ManifestOf(request, name);
        var insecure = request["allowInsecureTransport"]?.GetValue<bool>() ?? false;
        var maxResponseSize = request["maxResponseSize"]?.GetValue<long>();
        var requestTimeout = request["requestTimeout"]?.GetValue<double>();
        var connectTimeout = request["connectTimeout"]?.GetValue<double>();
        var idleTimeout = request["idleTimeout"]?.GetValue<double>();
        GraphQLSubscriptionSocket? socket = null;
        try
        {
            var socketOptions = SocketOptions(
                request,
                insecure,
                maxResponseSize,
                connectTimeout,
                idleTimeout
            );
            socket = new GraphQLSubscriptionSocket(socketOptions);
            var serverOptions = ServerOptions(
                request,
                insecure,
                machine,
                socket,
                maxResponseSize,
                requestTimeout
            );
            var server = new GraphQLMachineServer(serverOptions);
            var newRequestId = RequestIdMinter(request["throwingRequestId"]?.GetValue<int>());
            var controller = new ServerMachineController(
                new ServerMachineOptions
                {
                    Machine = machine,
                    Server = server,
                    Predictor = MachinePredictor.FromIr(machine),
                    Id = request["instance"]?.GetValue<string>(),
                    StartInput = request["startInput"]?.DeepClone(),
                    NewRequestId = newRequestId,
                    RetryDelays = Delays(request["retryDelays"]),
                }
            );
            return new Built(socket, server, controller, newRequestId, Code: null);
        }
        // A client refusing cleartext to another host refuses to be built, before it sends anything, and so does one
        // given an option out of range.
        catch (ArgumentException ex)
        {
            if (socket is not null)
                _ = socket.DisposeAsync().AsTask();
            return new Built(
                Socket: null,
                Server: null,
                Controller: null,
                NewRequestId: null,
                ex.Message.Contains("not over TLS", StringComparison.Ordinal)
                    ? "INSECURE_TRANSPORT"
                    : "INVALID_OPTION"
            );
        }
    }

    private GraphQLMachineServerOptions ServerOptions(
        JsonObject request,
        bool insecure,
        ClientManifestMachine machine,
        GraphQLSubscriptionSocket socket,
        long? maxResponseSize,
        double? requestTimeout
    )
    {
        var serverOptions = new GraphQLMachineServerOptions
        {
            Http = _http,
            Endpoint = new Uri(request["endpoint"].NotNull().GetValue<string>()),
            AllowInsecureTransport = insecure,
            Machine = machine,
            Socket = socket,
            ResubscribeDelays = Delays(request["resubscribeDelays"]),
        };
        if (maxResponseSize is { } max)
            serverOptions = serverOptions with { MaxResponseSize = max };
        if (requestTimeout is { } ms)
            serverOptions = serverOptions with { RequestTimeout = TimeSpan.FromMilliseconds(ms) };
        return serverOptions;
    }

    private static ClientManifestMachine ManifestOf(JsonObject request, string name) =>
        new ClientManifestMachine
        {
            Id = name,
            IrHash = request["irHash"].NotNull().GetValue<string>(),
            Ir = (JsonObject)request["ir"].NotNull().DeepClone(),
            Documents = request["documents"]
                .NotNull()
                .AsArray()
                .Select(d => new ClientManifestDocument
                {
                    Id = d.NotNull()["id"].NotNull().GetValue<string>(),
                    Operation = d["operation"].NotNull().GetValue<string>(),
                    Document = d["document"].NotNull().GetValue<string>(),
                })
                .ToList(),
        };

    private static GraphQLSubscriptionSocketOptions SocketOptions(
        JsonObject request,
        bool insecure,
        long? maxResponseSize,
        double? connectTimeout,
        double? idleTimeout
    )
    {
        // Only what the transcript sets is set: every other option keeps the client's default, as an app's would.
        var socketOptions = new GraphQLSubscriptionSocketOptions
        {
            Endpoint = new Uri(request["socket"].NotNull().GetValue<string>()),
            AllowInsecureTransport = insecure,
        };
        if (maxResponseSize is { } socketMax)
            socketOptions = socketOptions with { MaxMessageSize = (int)socketMax };
        if (connectTimeout is { } connect)
            socketOptions = socketOptions with
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(connect),
            };
        if (idleTimeout is { } idle)
            socketOptions = socketOptions with
            {
                KeepAliveTimeout = TimeSpan.FromMilliseconds(idle),
            };
        return socketOptions;
    }

    private static Func<string> RequestIdMinter(int? throwingAt)
    {
        var minted = 0;
        var calls = 0;
        // Counts r1, r2, ... from each open; the call numbered throwingRequestId throws and mints nothing.
        Func<string> newRequestId = () =>
        {
            if (Interlocked.Increment(ref calls) == throwingAt)
                throw new InvalidOperationException("the request id minter threw");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"r{Interlocked.Increment(ref minted)}"
            );
        };
        return newRequestId;
    }

    // As an app builds one: redirects off, which the client requires, and every other setting, its timeout among
    // them, left as HttpClient has it.
    // A delegating handler before it stands for the headers an app sets on each request, a token it refreshes: while
    // the transcript has them throw, it throws before the request is sent.
    private HttpClient NewHttp() =>
        new(
            new HeadersHandler(this)
            {
                InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false },
            }
        );

    private sealed class HeadersHandler(ClientTestee testee) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            testee._headersThrow
                ? throw new InvalidOperationException("the headers provider threw")
                : base.SendAsync(request, cancellationToken);
    }

    // Delays the open request does not give are left unset, so the client's defaults are in force.
    private static IReadOnlyList<TimeSpan>? Delays(JsonNode? delays) =>
        (delays as JsonArray)
            ?.Select(d => TimeSpan.FromMilliseconds(d.NotNull().GetValue<double>()))
            .ToList();

    private static JsonObject OutcomeOf(SendOutcome outcome) =>
        outcome switch
        {
            SendOutcome.Confirmed c => new JsonObject
            {
                ["outcome"] = "confirmed",
                ["state"] = c.Snapshot.State,
                ["revision"] = c.Snapshot.Revision,
            },
            SendOutcome.Refused r => new JsonObject
            {
                ["outcome"] = "refused",
                ["code"] = r.Problem.Code,
            },
            SendOutcome.Failed f => new JsonObject
            {
                ["outcome"] = "failed",
                ["code"] = f.Problem.Code,
            },
            SendOutcome.Stopped s => new JsonObject
            {
                ["outcome"] = "stopped",
                ["code"] = s.Error.Code,
            },
            _ => new JsonObject { ["error"] = $"unknown outcome {outcome}" },
        };

    private static JsonObject SyncOf(SyncOutcome outcome) =>
        outcome switch
        {
            SyncOutcome.Synced s => new JsonObject
            {
                ["outcome"] = "synced",
                ["state"] = s.Snapshot.State,
                ["revision"] = s.Snapshot.Revision,
            },
            SyncOutcome.Superseded => new JsonObject { ["outcome"] = "superseded" },
            SyncOutcome.Unreachable => new JsonObject { ["outcome"] = "unreachable" },
            SyncOutcome.Exceeded => new JsonObject { ["outcome"] = "exceeded" },
            SyncOutcome.Invalid { Code: { } code } => new JsonObject
            {
                ["outcome"] = "invalid",
                ["code"] = code.ToWire(),
            },
            SyncOutcome.Invalid { Trigger: { } trigger } => new JsonObject
            {
                ["outcome"] = "invalid",
                ["trigger"] = trigger,
            },
            SyncOutcome.Invalid i => new JsonObject
            {
                ["outcome"] = "invalid",
                ["target"] = i.Target,
            },
            SyncOutcome.Refused r => new JsonObject
            {
                ["outcome"] = "refused",
                ["code"] = r.Problem.Code,
            },
            SyncOutcome.Failed f => new JsonObject
            {
                ["outcome"] = "failed",
                ["code"] = f.Problem.Code,
            },
            SyncOutcome.Stopped s => new JsonObject
            {
                ["outcome"] = "stopped",
                ["code"] = s.Error.Code,
            },
            _ => new JsonObject { ["error"] = $"unknown outcome {outcome}" },
        };

    private static JsonObject ViewOf(MachineView view) =>
        new()
        {
            ["status"] = view.Status.ToString().ToLowerInvariant(),
            ["instance"] = view.Id,
            ["state"] = view.Snapshot?.State,
            ["context"] = view.Snapshot?.Context.DeepClone(),
            ["confirmed"] = view.Confirmed is { } confirmed
                ? new JsonObject
                {
                    ["state"] = confirmed.State,
                    ["context"] = confirmed.Context.DeepClone(),
                }
                : null,
            ["rowId"] = view.RowId,
            ["revision"] = view.Revision,
            // In the offset the client holds it in: the runner compares it as the instant it names.
            ["dueAt"] = view.DueAt?.ToString("o", CultureInfo.InvariantCulture),
            ["pending"] = view.Pending,
            ["problem"] = view.Problem?.Code,
            ["error"] = view.Error?.Code,
            ["deleted"] = view.Error?.Deleted,
            ["unsaved"] = view.Unsaved,
        };

    private async Task CloseAsync()
    {
        _controller?.Dispose();
        _controller = null;
        _server = null;
        if (_socket is { } socket)
            await socket.DisposeAsync().ConfigureAwait(false);
        _socket = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
