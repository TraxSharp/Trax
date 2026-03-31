using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Samples.GameServer.E2E.Utilities;

/// <summary>
/// Minimal graphql-transport-ws protocol client for testing HotChocolate subscriptions.
/// </summary>
public class GraphQLWebSocketClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private WebSocket _webSocket = null!;
    private readonly List<string> _activeSubscriptions = [];

    public static async Task<GraphQLWebSocketClient> ConnectAsync(
        WebSocketClient wsClient,
        string path = "/trax/graphql",
        TimeSpan? timeout = null
    )
    {
        var client = new GraphQLWebSocketClient();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

        wsClient.ConfigureRequest = request =>
        {
            request.Headers["Sec-WebSocket-Protocol"] = "graphql-transport-ws";
        };

        var uri = new Uri($"ws://localhost{path}");
        client._webSocket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        // Send connection_init
        await client.SendMessageAsync(new { type = "connection_init" });

        // Wait for connection_ack
        var ack = await client.ReceiveMessageAsync(effectiveTimeout);
        var ackType = ack.GetProperty("type").GetString();
        if (ackType != "connection_ack")
            throw new InvalidOperationException($"Expected connection_ack but got {ackType}");

        return client;
    }

    public async Task SubscribeAsync(string id, string query)
    {
        _activeSubscriptions.Add(id);
        await SendMessageAsync(
            new
            {
                id,
                type = "subscribe",
                payload = new { query },
            }
        );
    }

    public async Task<JsonElement> ReceiveNextAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var message = await ReceiveMessageAsync(remaining);
            var type = message.GetProperty("type").GetString();

            if (type == "next")
                return message.GetProperty("payload");

            if (type == "error")
            {
                var errorPayload = message.GetProperty("payload");
                throw new InvalidOperationException($"Subscription error: {errorPayload}");
            }

            if (type == "complete")
                throw new InvalidOperationException("Subscription completed unexpectedly");

            // Ignore other message types (ping, pong, etc.)
        }

        throw new TimeoutException(
            $"No subscription event received within {effectiveTimeout.TotalSeconds}s"
        );
    }

    public async Task<bool> TryReceiveNextAsync(TimeSpan timeout)
    {
        try
        {
            await ReceiveNextAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task SendMessageAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

    private async Task<JsonElement> ReceiveMessageAsync(TimeSpan timeout)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("WebSocket closed by server");

            ms.Position = 0;
            return JsonSerializer.Deserialize<JsonElement>(ms.ToArray(), JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"No WebSocket message received within {timeout.TotalSeconds}s"
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Test complete",
                    CancellationToken.None
                );
            }
            catch
            {
                // Ignore close errors in test cleanup
            }
        }

        _webSocket.Dispose();
    }
}
