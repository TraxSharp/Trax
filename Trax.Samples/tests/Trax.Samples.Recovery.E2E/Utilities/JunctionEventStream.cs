using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// One <c>graphql-transport-ws</c> connection that subscribes to <c>onJunctionEvent</c> for any number
/// of runs and keeps every step it receives, read in the background.
/// </summary>
public sealed class JunctionEventStream : IAsyncDisposable
{
    public const string StepFields = """
        eventType
        metadataId
        junction {
          position kind name state questionKey answer confidence replayed
          answerWithheld nameWithheld trackPosition attempt failureException
        }
        """;

    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly ConcurrentDictionary<long, ConcurrentQueue<JsonElement>> _steps = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private Task _reader = Task.CompletedTask;

    private JunctionEventStream(WebSocket socket) => _socket = socket;

    /// <summary>Opens the connection, authenticated with <paramref name="apiKey"/> when given.</summary>
    public static async Task<JunctionEventStream> ConnectAsync(
        WebSocketClient client,
        string? apiKey
    )
    {
        client.ConfigureRequest = request =>
            request.Headers["Sec-WebSocket-Protocol"] = "graphql-transport-ws";
        var socket = await client.ConnectAsync(
            new Uri("ws://localhost/trax/graphql"),
            CancellationToken.None
        );
        var stream = new JunctionEventStream(socket);

        // Browsers cannot set headers on a WebSocket upgrade, so the key travels in connection_init.
        await stream.SendAsync(
            apiKey is null
                ? new { type = "connection_init" }
                : new { type = "connection_init", payload = (object)new { apiKey } }
        );
        var ack = await stream.ReceiveAsync(TimeSpan.FromSeconds(5));
        if (ack.GetProperty("type").GetString() != "connection_ack")
            throw new InvalidOperationException($"Expected connection_ack, got {ack}");

        stream._reader = Task.Run(stream.ReadLoop);
        return stream;
    }

    /// <summary>Subscribes to the steps of the run <paramref name="metadataId"/>.</summary>
    public Task SubscribeAsync(long metadataId)
    {
        _steps.TryAdd(metadataId, new ConcurrentQueue<JsonElement>());
        return SendAsync(
            new
            {
                id = metadataId.ToString(),
                type = "subscribe",
                payload = new
                {
                    query = $"subscription {{ onJunctionEvent(metadataId: {metadataId}) {{ {StepFields} }} }}",
                },
            }
        );
    }

    /// <summary>Every step received so far for the run, in arrival order.</summary>
    public IReadOnlyList<JsonElement> StepsOf(long metadataId) =>
        _steps.TryGetValue(metadataId, out var queue) ? queue.ToList() : [];

    /// <summary>Errors the server sent for a subscription.</summary>
    public IReadOnlyList<string> Errors => _errors.ToList();

    private async Task ReadLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveAsync(Timeout.InfiniteTimeSpan, _stop.Token);
                var type = message.GetProperty("type").GetString();
                if (type == "ping")
                    await SendAsync(new { type = "pong" });
                else if (type == "error")
                    _errors.Enqueue(message.ToString());
                else if (
                    type == "next"
                    && long.TryParse(message.GetProperty("id").GetString(), out var id)
                )
                {
                    var payload = message.GetProperty("payload");
                    if (payload.TryGetProperty("errors", out var errors))
                        _errors.Enqueue(errors.ToString());
                    else
                        _steps
                            .GetOrAdd(id, _ => new ConcurrentQueue<JsonElement>())
                            .Enqueue(payload.GetProperty("data").GetProperty("onJunctionEvent"));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private async Task SendAsync(object message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _sending.WaitAsync();
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sending.Release();
        }
    }

    private async Task<JsonElement> ReceiveAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
            cts.CancelAfter(timeout);

        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Closed by the server.");
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonSerializer.Deserialize<JsonElement>(ms.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    CancellationToken.None
                );
        }
        catch (WebSocketException) { }
        await _reader;
        _socket.Dispose();
        _stop.Dispose();
        _sending.Dispose();
    }
}
