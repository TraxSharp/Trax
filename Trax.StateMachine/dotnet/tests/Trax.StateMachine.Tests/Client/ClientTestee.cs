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
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false });
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
        _socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = new Uri(request["socket"]!.GetValue<string>()),
            }
        );
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = _http,
                Endpoint = new Uri(request["endpoint"]!.GetValue<string>()),
                Machine = machine,
                Socket = _socket,
                ResubscribeDelays = Delays(request["resubscribeDelays"]),
            }
        );
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
        return new JsonObject { ["opened"] = true };
    }

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
