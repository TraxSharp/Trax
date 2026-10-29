using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// A graphql-transport-ws host on a loopback port, with no web host: each connection it accepts is handed to the next of
/// its scripts, the last one repeating.
/// </summary>
internal sealed class WireHost : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<WirePeer, Task>[] _scripts;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _peers = [];
    private readonly Task _accepting;
    private int _connections;

    public WireHost(params Func<WirePeer, Task>[] scripts)
    {
        _scripts = scripts;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public Uri Endpoint =>
        new($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/graphql");

    /// <summary>How many connections were accepted.</summary>
    public int Connections => Volatile.Read(ref _connections);

    private async Task AcceptAsync()
    {
        try
        {
            for (; ; )
            {
                var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                var n = Interlocked.Increment(ref _connections);
                var script = _scripts[Math.Min(n - 1, _scripts.Length - 1)];
                lock (_peers)
                    _peers.Add(RunAsync(tcp, script));
            }
        }
        catch (Exception ex)
            when (_stop.IsCancellationRequested
                && ex
                    is OperationCanceledException
                        or InvalidOperationException
                        or ObjectDisposedException
                        or SocketException
            )
        {
            // Disposed. A loop that comes back round after Stop() is told the listener is not listening.
        }
    }

    private async Task RunAsync(TcpClient tcp, Func<WirePeer, Task> script)
    {
        using (tcp)
        using (var peer = new WirePeer(tcp.GetStream(), _stop.Token))
        {
            try
            {
                await script(peer);
            }
            catch (Exception)
            {
                // The client went away, or the host is stopping.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _accepting;
        Task[] peers;
        lock (_peers)
            peers = [.. _peers];
        await Task.WhenAll(peers).WaitAsync(TimeSpan.FromSeconds(10));
        _stop.Dispose();
    }
}

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
        await ReadRequestAsync();
        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
            ),
            Stopping
        );
    }

    /// <summary>Accepts the upgrade, reads <c>connection_init</c> and acknowledges it.</summary>
    public async Task AckAsync()
    {
        await UpgradeAsync();
        var init = await ReceiveAsync();
        if (init?["type"]?.GetValue<string>() != "connection_init")
            throw new InvalidOperationException("The client did not send connection_init first.");
        await SendAsync("""{"type":"connection_ack"}""");
    }

    /// <summary>Accepts the upgrade and reads nothing yet.</summary>
    public async Task UpgradeAsync()
    {
        var key = (await ReadRequestAsync())
            .Split("\r\n")
            .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1]
            .Trim();
        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
        );
        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: graphql-transport-ws\r\n\r\n"
            ),
            Stopping
        );
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
            var result = await Socket.ReceiveAsync(buffer, Stopping);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            text.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return (JsonObject)JsonNode.Parse(text.ToArray())!;
        }
    }

    /// <summary>Reads until the next message of <paramref name="type"/>, and returns it.</summary>
    public async Task<JsonObject> ExpectAsync(string type)
    {
        for (; ; )
        {
            var message =
                await ReceiveAsync()
                ?? throw new InvalidOperationException($"Closed before a {type}.");
            if (message["type"]?.GetValue<string>() == type)
                return message;
        }
    }

    /// <summary>Reads the next subscribe message and returns its id.</summary>
    public async Task<string> SubscribedAsync() =>
        (await ExpectAsync("subscribe"))["id"]!.GetValue<string>();

    public Task SendAsync(string text) =>
        Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Stopping);

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
            while (await ReceiveAsync() is not null) { }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            // Dropped rather than closed.
        }
    }

    /// <summary>
    /// Reads until the client's close, never answers it, and waits until the client drops the connection itself.
    /// </summary>
    public async Task LeaveCloseUnansweredAsync()
    {
        while (await ReceiveAsync() is not null) { }
        var one = new byte[1];
        try
        {
            while (await stream.ReadAsync(one, Stopping) > 0) { }
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
            await Task.Delay(Timeout.Infinite, Stopping);
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
            if (await stream.ReadAsync(one, Stopping) == 0)
                throw new IOException("The client closed before its handshake ended.");
            request.Append((char)one[0]);
        }
        return request.ToString();
    }

    public void Dispose() => _socket?.Dispose();
}
