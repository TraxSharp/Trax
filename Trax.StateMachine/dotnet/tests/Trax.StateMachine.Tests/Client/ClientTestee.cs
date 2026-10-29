using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Client;
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
    private const string Client = "dotnet";

    private HttpClient _http = NewHttp(null);
    private ServerMachineController? _controller;
    private GraphQLSubscriptionSocket? _socket;

    /// <summary>Answers one request line with one answer line, once the request has settled.</summary>
    public async Task<string> AnswerAsync(string line)
    {
        var request = (JsonObject)JsonNode.Parse(line)!;
        JsonObject answer;
        try
        {
            answer = await HandleAsync(request);
        }
        catch (Exception ex)
        {
            answer = new JsonObject { ["error"] = $"threw: {ex.Message}" };
        }
        answer["id"] = request["id"]?.DeepClone();
        return answer.ToJsonString();
    }

    private async Task<JsonObject> HandleAsync(JsonObject request)
    {
        var op = request["op"]!.GetValue<string>();
        switch (op)
        {
            case "open":
                await CloseAsync();
                return Open(request);
            case "dispose":
                await CloseAsync();
                return new JsonObject { ["disposed"] = true };
            case "end":
                await CloseAsync();
                return new JsonObject();
        }
        if (_controller is not { } controller)
            return new JsonObject { ["error"] = "no client is open" };
        return op switch
        {
            "start" => await Settled(controller.StartAsync()),
            "send" => OutcomeOf(
                await controller.SendAsync(
                    request["trigger"]!.GetValue<string>(),
                    request["input"]?.DeepClone()
                )
            ),
            "retry" => OutcomeOf(await controller.RetryAsync()),
            "sync" => SyncOf(
                await controller.SyncToAsync(
                    request["target"]!.GetValue<string>(),
                    request["fields"] is JsonObject fields
                        ? new PlannedEvent(
                            fields["trigger"]!.GetValue<string>(),
                            fields["input"]?.DeepClone()
                        )
                        : null,
                    request["navigation"]!.AsArray().Select(t => t!.GetValue<string>()).ToList()
                )
            ),
            "view" => ViewOf(controller.View),
            _ => new JsonObject { ["unsupported"] = true },
        };
    }

    private static async Task<JsonObject> Settled(Task started)
    {
        await started;
        return new JsonObject { ["settled"] = true };
    }

    private JsonObject Open(JsonObject request)
    {
        var name = request["machine"]!.GetValue<string>();
        var machine = new ClientManifestMachine
        {
            Id = name,
            IrHash = request["irHash"]!.GetValue<string>(),
            Ir = (JsonObject)request["ir"]!.DeepClone(),
            Documents = request["documents"]!
                .AsArray()
                .Select(d => new ClientManifestDocument
                {
                    Id = d!["id"]!.GetValue<string>(),
                    Operation = d["operation"]!.GetValue<string>(),
                    Document = d["document"]!.GetValue<string>(),
                })
                .ToList(),
        };
        var insecure = request["allowInsecureTransport"]?.GetValue<bool>() ?? false;
        var maxResponseSize = request["maxResponseSize"]?.GetValue<long>();
        _http.Dispose();
        _http = NewHttp(request["requestTimeout"]?.GetValue<double>());
        GraphQLMachineServer server;
        try
        {
            _socket = new GraphQLSubscriptionSocket(
                new GraphQLSubscriptionSocketOptions
                {
                    Endpoint = new Uri(request["socket"]!.GetValue<string>()),
                    AllowInsecureTransport = insecure,
                    MaxMessageSize = (int?)maxResponseSize ?? 16 * 1024 * 1024,
                }
            );
            server = new GraphQLMachineServer(
                new GraphQLMachineServerOptions
                {
                    Http = _http,
                    Endpoint = new Uri(request["endpoint"]!.GetValue<string>()),
                    AllowInsecureTransport = insecure,
                    Machine = machine,
                    Socket = _socket,
                    ResubscribeDelays = Delays(request["resubscribeDelays"]),
                    MaxResponseSize = maxResponseSize ?? 16 * 1024 * 1024,
                }
            );
        }
        // A client refusing cleartext to another host refuses to be built, before it sends anything.
        catch (ArgumentException ex)
            when (ex.Message.Contains("not over TLS", StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["opened"] = false,
                ["client"] = Client,
                ["code"] = "INSECURE_TRANSPORT",
            };
        }
        var count = 0;
        _controller = new ServerMachineController(
            new ServerMachineOptions
            {
                Machine = machine,
                Server = server,
                Predictor = MachinePredictor.FromIr(machine),
                Id = request["instance"]?.GetValue<string>(),
                StartInput = request["startInput"]?.DeepClone(),
                NewRequestId = () => $"r{Interlocked.Increment(ref count)}",
                RetryDelays = Delays(request["retryDelays"]),
            }
        );
        return new JsonObject { ["opened"] = true, ["client"] = Client };
    }

    // Redirects off, as the client requires; a transcript's request timeout is the client's own.
    private static HttpClient NewHttp(double? timeoutMs) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = timeoutMs is { } ms
                ? TimeSpan.FromMilliseconds(ms)
                : TimeSpan.FromSeconds(100),
        };

    private static IReadOnlyList<TimeSpan> Delays(JsonNode? delays) =>
        (delays as JsonArray ?? [])
            .Select(d => TimeSpan.FromMilliseconds(d!.GetValue<double>()))
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
            SyncOutcome.Invalid => new JsonObject { ["outcome"] = "invalid" },
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
        if (_socket is { } socket)
            await socket.DisposeAsync();
        _socket = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _http.Dispose();
    }
}
