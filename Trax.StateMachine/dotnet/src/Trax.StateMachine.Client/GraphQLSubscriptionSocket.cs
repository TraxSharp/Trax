using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What a subscription delivers: one GraphQL result per event, then an error or the end.</summary>
public interface ISubscriptionSink
{
    /// <summary>One event: a GraphQL result, <c>data</c> or <c>errors</c>.</summary>
    void Next(JsonObject value);

    /// <summary>The subscription failed: the server refused it, or the socket closed.</summary>
    void Error(Exception error);

    /// <summary>The server ended the subscription.</summary>
    void Complete();
}

/// <summary>A socket that subscribes to a persisted document by its id. The returned handle ends the subscription.</summary>
public interface ISubscriptionSocket
{
    /// <summary>Subscribes to the persisted document <paramref name="documentId"/> with <paramref name="variables"/>.</summary>
    IDisposable Subscribe(string documentId, JsonObject variables, ISubscriptionSink sink);
}

/// <summary>How a <see cref="GraphQLSubscriptionSocket"/> connects.</summary>
public sealed record GraphQLSubscriptionSocketOptions
{
    /// <summary>
    /// The host's socket endpoint (<c>wss://host/trax/graphql</c>). It must be <c>wss</c> unless its host is loopback
    /// or <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Accepts a <c>ws</c> endpoint on a host that is not loopback. Off by default: the <c>connection_init</c> payload,
    /// and any header set in <see cref="ConfigureSocket"/>, would travel in cleartext.
    /// </summary>
    public bool AllowInsecureTransport { get; init; }

    /// <summary>
    /// The payload of <c>connection_init</c>, read on every connection so a rotating token stays fresh; how a host
    /// authenticates a socket. Null sends none.
    /// </summary>
    public Func<CancellationToken, ValueTask<JsonObject?>>? ConnectionParams { get; init; }

    /// <summary>Sets up each socket before it connects: headers, a proxy, client certificates.</summary>
    public Action<ClientWebSocketOptions>? ConfigureSocket { get; init; }

    /// <summary>How long a connection may take to be acknowledged before it counts as failed.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// A graphql-transport-ws socket that subscribes by persisted id: each subscribe message carries the document's id and
/// its variables, never a document. It connects when the first subscription opens and closes when the last one ends.
/// It does not reconnect by itself: a closed socket fails every open subscription, and whoever subscribed decides when
/// to subscribe again (<see cref="GraphQLMachineServer"/> does, after its resubscribe delays).
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/dotnet-client">SDK reference</seealso>
public sealed class GraphQLSubscriptionSocket : ISubscriptionSocket, IAsyncDisposable
{
    private const string Protocol = "graphql-transport-ws";

    private readonly GraphQLSubscriptionSocketOptions _options;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private Connection? _connection;
    private long _next;
    private bool _disposed;

    /// <summary>Builds the socket. It connects on the first subscription.</summary>
    /// <exception cref="ArgumentException">
    /// The endpoint is not <c>wss</c> on a host that is not loopback, and
    /// <see cref="GraphQLSubscriptionSocketOptions.AllowInsecureTransport"/> is not set.
    /// </exception>
    public GraphQLSubscriptionSocket(GraphQLSubscriptionSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TransportSecurity.RequireSecure(
            options.Endpoint,
            options.AllowInsecureTransport,
            "wss",
            nameof(options)
        );
        _options = options;
    }

    // One connection: its socket, whether it was acknowledged, and its subscriptions by id.
    private sealed class Connection
    {
        public required ClientWebSocket Socket { get; init; }
        public required Task Ready { get; init; }
        public Dictionary<string, ISubscriptionSink> Sinks { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Stop { get; } = new();
        public bool Closed { get; set; }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(string documentId, JsonObject variables, ISubscriptionSink sink)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(sink);
        Connection connection;
        string id;
        lock (_gate)
        {
            if (_disposed)
            {
                sink.Error(new ObjectDisposedException(nameof(GraphQLSubscriptionSocket)));
                return new Handle(() => { });
            }
            connection = _connection is { Closed: false } open ? open : _connection = Connect();
            id = (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            connection.Sinks[id] = sink;
        }
        _ = SubscribeAsync(connection, id, documentId, (JsonObject)variables.DeepClone());
        return new Handle(() => Unsubscribe(connection, id));
    }

    private async Task SubscribeAsync(
        Connection connection,
        string id,
        string documentId,
        JsonObject variables
    )
    {
        try
        {
            await connection.Ready.ConfigureAwait(false);
            await SendAsync(
                    connection,
                    new JsonObject
                    {
                        ["id"] = id,
                        ["type"] = "subscribe",
                        ["payload"] = new JsonObject
                        {
                            ["id"] = documentId,
                            ["variables"] = variables,
                        },
                    }
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Fail(connection, ex);
        }
    }

    private void Unsubscribe(Connection connection, string id)
    {
        bool last;
        lock (_gate)
        {
            if (!connection.Sinks.Remove(id))
                return;
            last = connection.Sinks.Count == 0;
        }
        if (connection.Closed)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await connection.Ready.ConfigureAwait(false);
                await SendAsync(connection, new JsonObject { ["id"] = id, ["type"] = "complete" })
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The socket is gone already; there is nothing left to end.
            }
            if (last)
                CloseIfIdle(connection);
        });
    }

    // Closes a connection nobody subscribes over any more.
    private void CloseIfIdle(Connection connection)
    {
        lock (_gate)
        {
            if (connection.Sinks.Count > 0 || connection.Closed)
                return;
            connection.Closed = true;
            if (_connection == connection)
                _connection = null;
        }
        _ = CloseAsync(connection);
    }

    private static async Task CloseAsync(Connection connection)
    {
        try
        {
            await connection
                .Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Already closed.
        }
        connection.Stop.Cancel();
        connection.Socket.Dispose();
    }

    private Connection Connect()
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(Protocol);
        _options.ConfigureSocket?.Invoke(socket.Options);
        var acknowledged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var connection = new Connection { Socket = socket, Ready = acknowledged.Task };
        _ = RunAsync(connection, acknowledged);
        return connection;
    }

    private async Task RunAsync(Connection connection, TaskCompletionSource acknowledged)
    {
        try
        {
            using (
                var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Stop.Token)
            )
            {
                timeout.CancelAfter(_options.ConnectTimeout);
                await connection
                    .Socket.ConnectAsync(_options.Endpoint, timeout.Token)
                    .ConfigureAwait(false);
                var init = new JsonObject { ["type"] = "connection_init" };
                if (_options.ConnectionParams is { } parameters)
                    init["payload"] = await parameters(timeout.Token).ConfigureAwait(false);
                await SendAsync(connection, init).ConfigureAwait(false);
                var first = await ReceiveAsync(connection.Socket, timeout.Token)
                    .ConfigureAwait(false);
                if (first?["type"]?.GetValue<string>() != "connection_ack")
                    throw new WebSocketException(
                        CloseMessage(connection.Socket, "before it was acknowledged")
                    );
            }
            acknowledged.TrySetResult();

            for (; ; )
            {
                var message = await ReceiveAsync(connection.Socket, connection.Stop.Token)
                    .ConfigureAwait(false);
                if (message is null)
                    throw new WebSocketException(
                        CloseMessage(connection.Socket, "while subscribed")
                    );
                await OnMessageAsync(connection, message).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            acknowledged.TrySetException(ex);
            Fail(connection, ex);
        }
    }

    private static string CloseMessage(ClientWebSocket socket, string when) =>
        socket.CloseStatus is { } status
            ? $"The socket was closed {when}: {(int)status} {socket.CloseStatusDescription}".TrimEnd()
            : $"The socket was closed {when}.";

    private async Task OnMessageAsync(Connection connection, JsonObject message)
    {
        var type = message["type"]?.GetValue<string>();
        var id = message["id"]?.GetValue<string>();
        ISubscriptionSink? sink = null;
        if (id is not null)
            lock (_gate)
                sink = connection.Sinks.GetValueOrDefault(id);
        switch (type)
        {
            case "ping":
                await SendAsync(connection, new JsonObject { ["type"] = "pong" })
                    .ConfigureAwait(false);
                return;
            case "next" when sink is not null && message["payload"] is JsonObject payload:
                sink.Next(payload);
                return;
            case "error" when sink is not null:
                Remove(connection, id!);
                sink.Error(new SubscriptionErrorException(message["payload"] as JsonArray));
                return;
            case "complete" when sink is not null:
                Remove(connection, id!);
                sink.Complete();
                return;
        }
    }

    private void Remove(Connection connection, string id)
    {
        lock (_gate)
            connection.Sinks.Remove(id);
    }

    // The connection failed or closed: every subscription over it fails, and the next subscription connects again.
    private void Fail(Connection connection, Exception error)
    {
        List<ISubscriptionSink> sinks;
        lock (_gate)
        {
            connection.Closed = true;
            if (_connection == connection)
                _connection = null;
            sinks = connection.Sinks.Values.ToList();
            connection.Sinks.Clear();
        }
        connection.Stop.Cancel();
        foreach (var sink in sinks)
            sink.Error(error);
    }

    private async Task SendAsync(Connection connection, JsonObject message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _send.WaitAsync(connection.Stop.Token).ConfigureAwait(false);
        try
        {
            await connection
                .Socket.SendAsync(bytes, WebSocketMessageType.Text, true, connection.Stop.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _send.Release();
        }
    }

    private static async Task<JsonObject?> ReceiveAsync(
        ClientWebSocket socket,
        CancellationToken token
    )
    {
        var buffer = new byte[8192];
        using var text = new MemoryStream();
        for (; ; )
        {
            var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            text.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        try
        {
            return JsonNode.Parse(text.ToArray()) as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new WebSocketException("The server sent a message that is not JSON.", ex);
        }
    }

    /// <summary>Closes the socket. Every open subscription fails, and no new one connects.</summary>
    public async ValueTask DisposeAsync()
    {
        Connection? connection;
        lock (_gate)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
        }
        if (connection is null)
            return;
        Fail(connection, new ObjectDisposedException(nameof(GraphQLSubscriptionSocket)));
        await CloseAsync(connection).ConfigureAwait(false);
    }

    private sealed class Handle(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

/// <summary>The server ended a subscription with GraphQL errors.</summary>
public sealed class SubscriptionErrorException : Exception
{
    /// <summary>Builds the exception from the errors the server sent.</summary>
    public SubscriptionErrorException(JsonArray? errors)
        : base(
            errors?[0]?["message"] is JsonValue message && message.TryGetValue<string>(out var text)
                ? text
                : "The server ended the subscription with an error."
        ) => Errors = errors ?? [];

    /// <summary>The errors as the server sent them.</summary>
    public JsonArray Errors { get; }
}
