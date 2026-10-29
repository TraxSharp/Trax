using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Cli.Tests.IntegrationTests.OperationsConformance.Generated;
using Trax.Client;

namespace Trax.Cli.Tests.IntegrationTests.OperationsConformance;

/// <summary>
/// The C# operations client as a testee of the operations kit
/// (<c>Trax.StateMachine/conformance/operations/PROTOCOL.md</c>): the client <c>trax client --operations --lang
/// csharp</c> generated for the kit's fixture, committed under <c>Generated/</c>, sent through its extension methods on
/// a <see cref="TraxClient"/> over a <see cref="TraxHttpTransport"/> and a <see cref="GraphQLSubscriptionSocket"/>.
/// Variables arrive as JSON and are read into the generated records, and data goes back as those records write it, so
/// the records' names, enums and left-out fields are on the wire the host compares.
/// </summary>
internal sealed class OperationsTestee : IAsyncDisposable
{
    private readonly object _gate = new();
    private TraxClient? _client;
    private HttpClient? _http;
    private GraphQLSubscriptionSocket? _socket;
    private IDisposable? _stream;
    private List<JsonObject> _events = [];

    /// <summary>Answers one request line with its answer line, once the request settles.</summary>
    public async Task<string> AnswerAsync(string line)
    {
        var request = JsonNode.Parse(line)!.AsObject();
        var id = request["id"]!.GetValue<int>();
        JsonObject answer;
        try
        {
            answer = request["op"]!.GetValue<string>() switch
            {
                "open" => await OpenAsync(request),
                "send" => await SendAsync(request),
                "subscribe" => Subscribe(request),
                "events" => Events(),
                "unsubscribe" => Unsubscribe(),
                "dispose" => await CloseAsync(new JsonObject { ["disposed"] = true }),
                "end" => await CloseAsync([]),
                _ => new JsonObject { ["unsupported"] = true },
            };
        }
        catch (Exception ex)
        {
            answer = new JsonObject { ["error"] = ex.ToString() };
        }
        answer["id"] = id;
        return answer.ToJsonString();
    }

    private async Task<JsonObject> OpenAsync(JsonObject request)
    {
        await CloseAsync([]);
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        if (request["requestTimeout"] is { } timeout)
            http.Timeout = TimeSpan.FromMilliseconds(timeout.GetValue<int>());
        var insecure = request["allowInsecureTransport"]?.GetValue<bool>() ?? false;
        try
        {
            var socket = new GraphQLSubscriptionSocket(
                new GraphQLSubscriptionSocketOptions
                {
                    Endpoint = new Uri(
                        request["socket"]!.GetValue<string>(),
                        UriKind.RelativeOrAbsolute
                    ),
                    AllowInsecureTransport = insecure,
                }
            );
            var transport = new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = http,
                    Endpoint = new Uri(
                        request["endpoint"]!.GetValue<string>(),
                        UriKind.RelativeOrAbsolute
                    ),
                    AllowInsecureTransport = insecure,
                    MaxResponseSize =
                        request["maxResponseSize"]?.GetValue<long>()
                        ?? TraxHttpTransportOptions.DefaultMaxResponseSize,
                    Socket = socket,
                }
            );
            _http = http;
            _socket = socket;
            _client = new TraxClient(
                transport,
                new TraxClientOptions { ResubscribeDelays = [TimeSpan.FromMilliseconds(10)] }
            );
            return new JsonObject { ["opened"] = true };
        }
        catch (ArgumentException)
        {
            http.Dispose();
            return new JsonObject { ["opened"] = false, ["code"] = "INSECURE_TRANSPORT" };
        }
    }

    private Task<JsonObject> SendAsync(JsonObject request)
    {
        var client = _client ?? throw new InvalidOperationException("send before open");
        var variables = request["variables"];
        return request["operation"]!.GetValue<string>() switch
        {
            "Runs" => Answer(client.RunsAsync(Read<RunsVariables>(variables))),
            "Retry" => Answer(client.RetryAsync(Read<RetryVariables>(variables))),
            var other => throw new InvalidOperationException($"No document {other} to send."),
        };
    }

    private static async Task<JsonObject> Answer<T>(Task<TraxAnswer<T>> sending)
    {
        var answer = await sending;
        if (answer.Failure is { } failure)
            return new JsonObject
            {
                ["outcome"] = "failed",
                ["code"] = TraxTransportFailure.Code,
                ["status"] = failure.Status,
            };
        if (answer.Errors.Count > 0)
            return new JsonObject { ["outcome"] = "refused", ["code"] = answer.Code };
        return new JsonObject { ["outcome"] = "data", ["data"] = Write(answer.Data) };
    }

    private JsonObject Subscribe(JsonObject request)
    {
        var client = _client ?? throw new InvalidOperationException("subscribe before open");
        Unsubscribe();
        lock (_gate)
            _events = [];
        _stream = request["operation"]!.GetValue<string>() switch
        {
            "Changes" => client.Changes(Read<ChangesVariables>(request["variables"]), Record),
            var other => throw new InvalidOperationException($"No stream {other} to follow."),
        };
        return new JsonObject { ["subscribed"] = true };
    }

    private void Record<T>(TraxStreamEvent<T> streamEvent)
    {
        JsonObject recorded = streamEvent switch
        {
            TraxStreamEvent<T>.Next next => new() { ["next"] = Write(next.Data) },
            TraxStreamEvent<T>.Interrupted => new() { ["interrupted"] = true },
            TraxStreamEvent<T>.Resumed => new() { ["resumed"] = true },
            TraxStreamEvent<T>.Ended { Errors.Count: > 0 } ended => new()
            {
                ["ended"] = "refused",
                ["code"] = ended.Errors[0].Code,
            },
            TraxStreamEvent<T>.Ended { Failure: null } => new() { ["ended"] = "complete" },
            TraxStreamEvent<T>.Ended { Failure.CloseStatus: { } close } => new()
            {
                ["ended"] = "closed",
                ["close"] = close,
            },
            _ => new() { ["ended"] = "failed" },
        };
        lock (_gate)
            _events.Add(recorded);
    }

    private JsonObject Events()
    {
        lock (_gate)
            return new JsonObject
            {
                ["events"] = new JsonArray(_events.Select(e => (JsonNode?)e.DeepClone()).ToArray()),
            };
    }

    private JsonObject Unsubscribe()
    {
        _stream?.Dispose();
        _stream = null;
        return new JsonObject { ["unsubscribed"] = true };
    }

    private async Task<JsonObject> CloseAsync(JsonObject answer)
    {
        Unsubscribe();
        _client = null;
        if (_socket is { } socket)
            await socket.DisposeAsync();
        _socket = null;
        _http?.Dispose();
        _http = null;
        return answer;
    }

    private static T Read<T>(JsonNode? variables) =>
        (variables ?? new JsonObject()).Deserialize<T>(TraxJson.Options)
        ?? throw new InvalidOperationException($"The variables did not read as {typeof(T).Name}.");

    private static JsonNode? Write<T>(T value) =>
        JsonSerializer.SerializeToNode(value, TraxJson.Options);

    public async ValueTask DisposeAsync() => await CloseAsync([]);
}
