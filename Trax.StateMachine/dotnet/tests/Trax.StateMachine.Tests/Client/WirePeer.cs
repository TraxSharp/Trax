using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Tests.Client;

/// <summary>One connection to a <see cref="WireHost"/>, as its script drives it.</summary>
internal sealed class WirePeer(NetworkStream stream, CancellationToken stopping) : IDisposable
{
    private WebSocket? _socket;

    /// <summary>Cancelled when the host is disposed.</summary>
    public CancellationToken Stopping { get; } = stopping;

    private WebSocket Socket => _socket ?? throw new InvalidOperationException("Not accepted.");

    /// <summary>Answers the upgrade with an HTTP status, and opens no socket.</summary>
    public async Task RefuseAsync(int status, string reason)
    {
        await ReadRequestAsync().ConfigureAwait(false);
        await stream
            .WriteAsync(
                Encoding.ASCII.GetBytes(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                    )
                ),
                Stopping
            )
            .ConfigureAwait(false);
    }

    /// <summary>Accepts the upgrade, reads <c>connection_init</c> and acknowledges it.</summary>
    public async Task AckAsync()
    {
        await UpgradeAsync().ConfigureAwait(false);
        var init = await ReceiveAsync().ConfigureAwait(false);
        if (
            !string.Equals(
                init?["type"]?.GetValue<string>(),
                "connection_init",
                StringComparison.Ordinal
            )
        )
            throw new InvalidOperationException("The client did not send connection_init first.");
        await SendAsync("""{"type":"connection_ack"}""").ConfigureAwait(false);
    }

    /// <summary>Accepts the upgrade and reads nothing yet.</summary>
    public async Task UpgradeAsync()
    {
        var key = (await ReadRequestAsync().ConfigureAwait(false))
            .Split("\r\n")
            .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1]
            .Trim();
        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
        );
        await stream
            .WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                        + $"Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: graphql-transport-ws\r\n\r\n"
                ),
                Stopping
            )
            .ConfigureAwait(false);
        _socket = WebSocket.CreateFromStream(
            stream,
            new WebSocketCreationOptions { IsServer = true, SubProtocol = "graphql-transport-ws" }
        );
    }

    /// <summary>The next message, or null once the client closed the socket.</summary>
    public async Task<JsonObject?> ReceiveAsync()
    {
        var buffer = new byte[64 * 1024];
        using var text = new MemoryStream();
        for (; ; )
        {
            var result = await Socket.ReceiveAsync(buffer, Stopping).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            await text.WriteAsync(buffer.AsMemory(0, result.Count), CancellationToken.None)
                .ConfigureAwait(false);
            if (result.EndOfMessage)
                return (JsonObject)JsonNode.Parse(text.ToArray()).NotNull();
        }
    }

    /// <summary>Reads until the next message of <paramref name="type"/>, and returns it.</summary>
    public async Task<JsonObject> ExpectAsync(string type)
    {
        for (; ; )
        {
            var message =
                await ReceiveAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Closed before a {type}.");
            if (string.Equals(message["type"]?.GetValue<string>(), type, StringComparison.Ordinal))
                return message;
        }
    }

    /// <summary>Reads the next subscribe message and returns its id.</summary>
    public async Task<string> SubscribedAsync() =>
        (await ExpectAsync("subscribe").ConfigureAwait(false))["id"].NotNull().GetValue<string>();

    public Task SendAsync(string text) =>
        Socket.SendAsync(
            Encoding.UTF8.GetBytes(text),
            WebSocketMessageType.Text,
            endOfMessage: true,
            Stopping
        );

    public Task NextAsync(string id, JsonObject payload) =>
        SendAsync(
            new JsonObject
            {
                ["id"] = id,
                ["type"] = "next",
                ["payload"] = payload,
            }.ToJsonString()
        );

    /// <summary>Waits until the client closes or drops the connection.</summary>
    public async Task GoneAsync()
    {
        try
        {
            while (await ReceiveAsync().ConfigureAwait(false) is not null) { }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // Dropped rather than closed.
        }
    }

    /// <summary>
    /// Reads until the client closes the socket, answers its close, and returns the status it closed with: null when it
    /// dropped the connection with no close frame.
    /// </summary>
    public async Task<WebSocketCloseStatus?> ClosedWithAsync()
    {
        try
        {
            while (await ReceiveAsync().ConfigureAwait(false) is not null) { }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            return null;
        }
        var status = Socket.CloseStatus;
        try
        {
            await Socket
                .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", Stopping)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // The client let go before the answer.
        }
        return status;
    }

    /// <summary>
    /// Reads until the client's close, never answers it, and waits until the client drops the connection itself.
    /// </summary>
    public async Task LeaveCloseUnansweredAsync()
    {
        while (await ReceiveAsync().ConfigureAwait(false) is not null) { }
        var one = new byte[1];
        try
        {
            while (await stream.ReadAsync(one, Stopping).ConfigureAwait(false) > 0) { }
        }
        catch (IOException)
        {
            // Dropped rather than closed.
        }
    }

    /// <summary>Holds the connection open, reading nothing, until the host is disposed.</summary>
    public async Task StallAsync()
    {
        try
        {
            // allowed-delay: holds the connection until the host is disposed.
            await Task.Delay(Timeout.Infinite, Stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>Drops the connection with no close frame, after what was sent so far.</summary>
    public void Drop()
    {
        stream.Socket.Shutdown(System.Net.Sockets.SocketShutdown.Send);
        stream.Socket.Close();
    }

    private async Task<string> ReadRequestAsync()
    {
        var request = new StringBuilder();
        var one = new byte[1];
        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one, Stopping).ConfigureAwait(false) == 0)
                throw new IOException("The client closed before its handshake ended.");
            request.Append((char)one[0]);
        }
        return request.ToString();
    }

    public void Dispose() => _socket?.Dispose();
}
