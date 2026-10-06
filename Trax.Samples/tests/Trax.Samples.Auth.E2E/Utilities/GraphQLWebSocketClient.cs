using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Samples.Auth.E2E.Utilities;

/// <summary>
/// A minimal graphql-transport-ws client. The credential travels in the <c>connection_init</c>
/// payload, because a browser cannot attach an Authorization header to a WebSocket upgrade.
/// </summary>
public sealed class GraphQLWebSocketClient : IAsyncDisposable
{
    private WebSocket _socket = null!;

    /// <summary>
    /// Opens a socket and sends <c>connection_init</c> with <paramref name="payload"/>. Throws
    /// <see cref="ConnectionRejectedException"/> when the server does not acknowledge it.
    /// </summary>
    public static async Task<GraphQLWebSocketClient> ConnectAsync(
        WebSocketClient wsClient,
        object? payload
    )
    {
        var client = new GraphQLWebSocketClient();
        wsClient.ConfigureRequest = request =>
            request.Headers["Sec-WebSocket-Protocol"] = "graphql-transport-ws";
        client._socket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/trax/graphql"),
            CancellationToken.None
        );

        await client.SendAsync(
            payload is null
                ? new { type = "connection_init" }
                : new { type = "connection_init", payload }
        );

        JsonElement ack;
        try
        {
            ack = await client.ReceiveAsync(TimeSpan.FromSeconds(10));
        }
        catch (WebSocketClosedException closed)
        {
            await client.DisposeAsync();
            throw new ConnectionRejectedException(closed.Message);
        }

        if (ack.GetProperty("type").GetString() != "connection_ack")
        {
            await client.DisposeAsync();
            throw new ConnectionRejectedException(ack.GetRawText());
        }

        return client;
    }

    public Task SubscribeAsync(string id, string query) =>
        SendAsync(
            new
            {
                id,
                type = "subscribe",
                payload = new { query },
            }
        );

    /// <summary>The next message that is not a ping or pong.</summary>
    public async Task<JsonElement> ReceiveAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"No WebSocket message within {timeout.TotalSeconds}s.");

            var message = await ReceiveRawAsync(remaining);
            var type = message.GetProperty("type").GetString();
            if (type is not ("ping" or "pong"))
                return message;
        }
    }

    private async Task SendAsync(object message)
    {
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(message, JsonSerializerOptions.Web)
        );
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<JsonElement> ReceiveRawAsync(TimeSpan timeout)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketClosedException(
                        $"closed by the server: {result.CloseStatus} {result.CloseStatusDescription}"
                    );
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No WebSocket message within {timeout.TotalSeconds}s.");
        }

        return JsonSerializer.Deserialize<JsonElement>(ms.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    CancellationToken.None
                );
            }
            catch (WebSocketException)
            {
                // The server may already have gone; nothing to clean up.
            }
        }
        _socket.Dispose();
    }
}

public sealed class ConnectionRejectedException(string message) : Exception(message);

public sealed class WebSocketClosedException(string message) : Exception(message);
