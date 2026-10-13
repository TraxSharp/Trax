using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A <c>graphql-transport-ws</c> client over an <see cref="E2EHost"/>'s test server: it authenticates in
/// <c>connection_init</c> with an API key, subscribes, and reads each operation's messages in order. A message for
/// another operation is kept until that operation is read, so one socket can carry several subscriptions.
/// </summary>
/// <remarks>
/// Disposing it sends a close frame: a test-server socket closed only on the client side leaves the server's read
/// loop spinning for the rest of the run. Every wait is bounded, so a missing message fails the test instead of
/// hanging it (central ADR 0014).
/// </remarks>
public sealed class GraphQLWs : IAsyncDisposable
{
    /// <summary>How long a test waits for a message it expects, unless it says otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ConnectDeadline = TimeSpan.FromSeconds(30);

    private readonly WebSocket _socket;
    private readonly List<JsonElement> _pending = [];
    private readonly byte[] _buffer = new byte[64 * 1024];

    private GraphQLWs(WebSocket socket) => _socket = socket;

    /// <summary>The socket's state, for a test that expects the server to close it.</summary>
    public WebSocketState State => _socket.State;

    /// <summary>
    /// Connects to <paramref name="host"/>'s GraphQL endpoint and completes the handshake, authenticating with
    /// <paramref name="apiKey"/> (anonymous when null). Throws when the server refuses the connection.
    /// </summary>
    public static Task<GraphQLWs> ConnectAsync(
        IHost host,
        string? apiKey,
        CancellationToken cancellationToken = default
    ) =>
        ConnectWithPayloadAsync(
            host,
            apiKey is null ? new { } : (object)new { apiKey },
            cancellationToken
        );

    /// <summary>
    /// Connects and completes the handshake with <paramref name="payload"/> as the <c>connection_init</c> payload,
    /// for a scheme other than an API key (<c>new { authToken = jwt }</c>). Throws when the server refuses.
    /// </summary>
    public static async Task<GraphQLWs> ConnectWithPayloadAsync(
        IHost host,
        object payload,
        CancellationToken cancellationToken = default
    )
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ConnectDeadline);
        var socket = await client.ConnectAsync(
            new Uri("ws://localhost/trax/graphql"),
            deadline.Token
        );

        var ws = new GraphQLWs(socket);
        try
        {
            await ws.SendAsync(new { type = "connection_init", payload });
            var ack = await ws.ReceiveAnyAsync(DefaultTimeout);
            if (ack.GetProperty("type").GetString() != "connection_ack")
                throw new InvalidOperationException(
                    "The server did not acknowledge the connection: " + ack.GetRawText()
                );
            return ws;
        }
        catch
        {
            await ws.DisposeAsync();
            throw;
        }
    }

    /// <summary>Starts the operation <paramref name="id"/>: a subscription, or a query or mutation over the socket.</summary>
    public Task SubscribeAsync(string id, string query, object? variables = null) =>
        SendAsync(
            new
            {
                id,
                type = "subscribe",
                payload = variables is null ? (object)new { query } : new { query, variables },
            }
        );

    /// <summary>
    /// Starts the operation <paramref name="id"/> from the persisted document <paramref name="documentId"/>, with
    /// <paramref name="variables"/>.
    /// </summary>
    public Task SubscribePersistedAsync(string id, string documentId, object? variables = null) =>
        SendAsync(
            new
            {
                id,
                type = "subscribe",
                payload = variables is null
                    ? (object)new { id = documentId }
                    : new { id = documentId, variables },
            }
        );

    /// <summary>
    /// Drops the socket without a close frame, as a client that crashes or loses its network does. The client is
    /// unusable afterwards.
    /// </summary>
    public void Abort() => _socket.Abort();

    /// <summary>Ends the operation <paramref name="id"/> from the client side.</summary>
    public Task CompleteAsync(string id) => SendAsync(new { id, type = "complete" });

    /// <summary>
    /// The next message for operation <paramref name="id"/> (<c>next</c>, <c>error</c> or <c>complete</c>), waiting
    /// at most <paramref name="timeout"/>.
    /// </summary>
    public async Task<JsonElement> NextAsync(string id, TimeSpan? timeout = null)
    {
        var kept = _pending.FindIndex(m => IdOf(m) == id);
        if (kept >= 0)
        {
            var message = _pending[kept];
            _pending.RemoveAt(kept);
            return message;
        }

        using var deadline = new CancellationTokenSource(timeout ?? DefaultTimeout);
        while (true)
        {
            var message = await ReceiveAnyAsync(timeout ?? DefaultTimeout, deadline.Token);
            if (IdOf(message) == id)
                return message;
            _pending.Add(message);
        }
    }

    /// <summary>
    /// The <c>payload.data</c> of the next <c>next</c> message for <paramref name="id"/>. Throws, quoting the message,
    /// when the operation errors or completes instead.
    /// </summary>
    public async Task<JsonElement> NextDataAsync(string id, TimeSpan? timeout = null)
    {
        var message = await NextAsync(id, timeout);
        if (
            message.GetProperty("type").GetString() != "next"
            || !message.GetProperty("payload").TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null
        )
            throw new InvalidOperationException(
                $"Expected data for operation {id}, got: {message.GetRawText()}"
            );
        return data;
    }

    /// <summary>
    /// Whether any message arrives for <paramref name="id"/> within <paramref name="window"/>. Use it only to show
    /// that nothing more arrives after a later sentinel has been received, never as a wait for something.
    /// </summary>
    public async Task<JsonElement?> TryNextAsync(string id, TimeSpan window)
    {
        try
        {
            return await NextAsync(id, window);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Sends a message of the protocol as JSON.</summary>
    public Task SendAsync(object message) =>
        _socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );

    // The next message of any operation, answering the server's pings on the way.
    private async Task<JsonElement> ReceiveAnyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (true)
        {
            using var text = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await _socket.ReceiveAsync(_buffer, deadline.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException(
                        $"The server closed the socket ({received.CloseStatus}): {received.CloseStatusDescription}"
                    );
                text.Write(_buffer, 0, received.Count);
            } while (!received.EndOfMessage);

            if (text.Length == 0)
                continue;
            var root = JsonDocument.Parse(text.ToArray()).RootElement.Clone();
            switch (root.GetProperty("type").GetString())
            {
                case "ping":
                    await SendAsync(new { type = "pong" });
                    continue;
                case "pong":
                    continue;
                default:
                    return root;
            }
        }
    }

    private static string? IdOf(JsonElement message) =>
        message.TryGetProperty("id", out var id) ? id.GetString() : null;

    /// <summary>Closes the socket with a close frame, so the server's read loop ends.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }
        _socket.Dispose();
    }
}
