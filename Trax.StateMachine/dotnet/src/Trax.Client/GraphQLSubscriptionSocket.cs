using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>What a subscription delivers: one GraphQL result per event, then an error or the end.</summary>
public interface ISubscriptionSink
{
    /// <summary>One event: a GraphQL result, <c>data</c> or <c>errors</c>.</summary>
    void Next(JsonObject value);

    /// <summary>
    /// The subscription failed: the server refused it (<see cref="SubscriptionErrorException"/>), the socket closed or
    /// broke (<see cref="SocketClosedException"/> and others), or there is no socket to follow it over
    /// (<see cref="SocketUnavailableException"/>).
    /// </summary>
    void Error(Exception error);

    /// <summary>The server ended the subscription.</summary>
    void Complete();

    /// <summary>
    /// The subscribe message went out over an acknowledged connection. Nothing by default. A
    /// <see cref="GraphQLSubscriptionSocket"/> calls it before the subscription's first <see cref="Next"/>, and never
    /// calls one subscription's sink from two threads at once.
    /// </summary>
    void Started() { }
}

/// <summary>A socket that subscribes to a document. The returned handle ends the subscription.</summary>
public interface ISubscriptionSocket
{
    /// <summary>
    /// Subscribes with <paramref name="payload"/> as the subscribe message's payload: <c>id</c> or <c>query</c>, and
    /// <c>operationName</c> and <c>variables</c>.
    /// </summary>
    IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink);
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

    /// <summary>
    /// Sets up each socket before it connects: headers, a proxy, client certificates. It runs after the keep-alive is
    /// set, so it may change that too.
    /// </summary>
    public Action<ClientWebSocketOptions>? ConfigureSocket { get; init; }

    /// <summary>
    /// How often the socket pings the host; 15 seconds by default. <see cref="Timeout.InfiniteTimeSpan"/> or zero sends
    /// no pings. Not set in a browser, which keeps its sockets alive itself.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a ping may go unanswered before the connection counts as broken, and every subscription over it is
    /// failed so its subscriber can subscribe again; 15 seconds by default. A host that drops off the network without
    /// closing the socket is found this way. <see cref="Timeout.InfiniteTimeSpan"/> waits forever.
    /// </summary>
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a connection may take to be acknowledged before it counts as failed.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The largest message the server may send, in bytes; 16 MiB by default. A larger one fails the connection, and
    /// every subscription over it, rather than being read into memory.
    /// </summary>
    public int MaxMessageSize { get; init; } = 16 * 1024 * 1024;
}

/// <summary>
/// A graphql-transport-ws socket. Each subscribe message carries the payload it is given, a persisted document's id or
/// a document's text. It connects when the first subscription opens and closes when the last one ends. It does not
/// reconnect by itself: a closed or broken socket fails every open subscription, with a
/// <see cref="SocketClosedException"/> when it closed, and whoever subscribed decides when to subscribe again
/// (<see cref="TraxClient"/> does, after its resubscribe delays).
/// </summary>
/// <remarks>
/// A sink that throws ends its own subscription: the socket completes it with the host and hands the exception to the
/// sink's <see cref="ISubscriptionSink.Error"/>. The other subscriptions over the connection go on.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/client-api/dotnet-client">SDK reference</seealso>
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
        public Dictionary<string, Subscription> Sinks { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Stop { get; } = new();
        public bool Closed { get; set; }

        /// <summary>The host sent a message over the limit, whose rest is never read.</summary>
        public bool Overflowed { get; set; }
    }

    // One subscription. Its sink is called under Gate, so its callbacks never overlap, and Started always comes first.
    private sealed class Subscription(string id, ISubscriptionSink sink)
    {
        public string Id { get; } = id;
        public ISubscriptionSink Sink { get; } = sink;
        public object Gate { get; } = new();
        public bool Started { get; set; }

        // Set once the sink has had its last callback, or its handle was disposed. Read without Gate by a disposal,
        // which must not wait on a callback that may be waiting on it.
        private volatile bool _ended;
        public bool Ended
        {
            get => _ended;
            set => _ended = value;
        }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(sink);
        Connection? connection = null;
        Subscription subscription;
        lock (_gate)
        {
            var id = (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            subscription = new Subscription(id, sink);
            if (!_disposed)
            {
                connection = _connection is { Closed: false } open ? open : _connection = Connect();
                connection.Sinks[id] = subscription;
            }
        }
        if (connection is null)
        {
            End(
                subscription,
                new SocketUnavailableException(
                    "The socket was disposed: it follows no more subscriptions."
                )
            );
            return new Handle(() => { });
        }
        _ = SubscribeAsync(connection, subscription, (JsonObject)payload.DeepClone());
        return new Handle(() => Unsubscribe(connection, subscription));
    }

    private async Task SubscribeAsync(
        Connection connection,
        Subscription subscription,
        JsonObject payload
    )
    {
        try
        {
            await connection.Ready.ConfigureAwait(false);
            await SendAsync(
                    connection,
                    new JsonObject
                    {
                        ["id"] = subscription.Id,
                        ["type"] = "subscribe",
                        ["payload"] = payload,
                    }
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Fail(connection, ex);
            return;
        }
        Deliver(connection, subscription, null);
    }

    private void Unsubscribe(Connection connection, Subscription subscription)
    {
        subscription.Ended = true;
        var id = subscription.Id;
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
        if (!OperatingSystem.IsBrowser())
        {
            socket.Options.KeepAliveInterval = _options.KeepAliveInterval;
            socket.Options.KeepAliveTimeout = _options.KeepAliveTimeout;
            // Keeps the upgrade's status, so a host that refuses the upgrade (401, 403) is told from one that is down.
            socket.Options.CollectHttpResponseDetails = true;
        }
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
                await ConnectAsync(connection.Socket, timeout.Token).ConfigureAwait(false);
                var init = new JsonObject { ["type"] = "connection_init" };
                if (_options.ConnectionParams is { } parameters)
                    init["payload"] = await parameters(timeout.Token).ConfigureAwait(false);
                await SendAsync(connection, init).ConfigureAwait(false);
                var first = await ReceiveAsync(connection, timeout.Token).ConfigureAwait(false);
                if (first is null)
                    throw Closed(connection.Socket, "before it was acknowledged");
                if (Text(first, "type") != "connection_ack")
                    throw new WebSocketException(
                        "The server answered connection_init with something other than connection_ack."
                    );
            }
            acknowledged.TrySetResult();

            for (; ; )
            {
                var message = await ReceiveAsync(connection, connection.Stop.Token)
                    .ConfigureAwait(false);
                if (message is null)
                    throw Closed(connection.Socket, "while subscribed");
                await OnMessageAsync(connection, message).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            acknowledged.TrySetException(ex);
            Fail(connection, ex);
            // The rest of a message over the limit is never read. The connection fails first, so a send
            // racing the abort cannot fail the subscriptions with the aborted socket's error instead.
            if (connection.Overflowed)
                connection.Socket.Abort();
            connection.Socket.Dispose();
        }
    }

    // Connects, telling a host that refused the upgrade (401, 403) from one that could not be reached.
    private async Task ConnectAsync(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            await socket.ConnectAsync(_options.Endpoint, token).ConfigureAwait(false);
        }
        catch (WebSocketException)
            when (socket.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var status = (int)socket.HttpStatusCode;
            throw new SocketClosedException(
                $"The host refused the socket's upgrade: {status} {socket.HttpStatusCode}.",
                null,
                status
            );
        }
    }

    private static SocketClosedException Closed(ClientWebSocket socket, string when) =>
        socket.CloseStatus is { } status
            ? new SocketClosedException(
                $"The socket was closed {when}: {(int)status} {socket.CloseStatusDescription}".TrimEnd(),
                (int)status
            )
            : new SocketClosedException($"The socket was closed {when}.", null);

    private async Task OnMessageAsync(Connection connection, JsonObject message)
    {
        var type =
            Text(message, "type")
            ?? throw new WebSocketException("The server sent a message with no type.");
        var id = Text(message, "id");
        Subscription? subscription = null;
        if (id is not null)
            lock (_gate)
                subscription = connection.Sinks.GetValueOrDefault(id);
        switch (type)
        {
            case "ping":
                await SendAsync(connection, new JsonObject { ["type"] = "pong" })
                    .ConfigureAwait(false);
                return;
            case "next" when subscription is not null && message["payload"] is JsonObject payload:
                Deliver(connection, subscription, payload);
                return;
            case "error" when subscription is not null:
                Remove(connection, id!);
                End(subscription, new SubscriptionErrorException(message["payload"] as JsonArray));
                return;
            case "complete" when subscription is not null:
                Remove(connection, id!);
                End(subscription, null);
                return;
        }
    }

    // A member of a message: null when it is absent, and a malformed message when it is there but not a string.
    private static string? Text(JsonObject message, string name) =>
        message[name] switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => throw new WebSocketException(
                $"The server sent a message whose {name} is not a string."
            ),
        };

    // Started, once and first, then the event (none when the subscribe message just went out). A sink that throws
    // ends its own subscription with that exception, and no other.
    private void Deliver(Connection connection, Subscription subscription, JsonObject? next)
    {
        Exception? thrown = null;
        lock (subscription.Gate)
        {
            if (subscription.Ended)
                return;
            try
            {
                if (!subscription.Started)
                {
                    subscription.Started = true;
                    subscription.Sink.Started();
                }
                if (next is not null && !subscription.Ended)
                    subscription.Sink.Next(next);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }
        if (thrown is null)
            return;
        End(subscription, thrown);
        Unsubscribe(connection, subscription);
    }

    // The sink's last call: Error with the error, or Complete when there is none. A throw from it has nowhere to go.
    private static void End(Subscription subscription, Exception? error)
    {
        lock (subscription.Gate)
        {
            if (subscription.Ended)
                return;
            subscription.Ended = true;
            try
            {
                if (error is null)
                    subscription.Sink.Complete();
                else
                    subscription.Sink.Error(error);
            }
            catch (Exception)
            {
                // The subscription is over; the sink was told the one thing left to tell it.
            }
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
        List<Subscription> subscriptions;
        lock (_gate)
        {
            connection.Closed = true;
            if (_connection == connection)
                _connection = null;
            subscriptions = connection.Sinks.Values.ToList();
            connection.Sinks.Clear();
        }
        connection.Stop.Cancel();
        foreach (var subscription in subscriptions)
            End(subscription, error);
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

    private async Task<JsonObject?> ReceiveAsync(Connection connection, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var text = new MemoryStream();
        for (; ; )
        {
            var result = await connection.Socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (text.Length + result.Count > _options.MaxMessageSize)
            {
                connection.Overflowed = true;
                throw new WebSocketException(
                    $"The server sent a message larger than {_options.MaxMessageSize} bytes."
                );
            }
            text.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(text.ToArray());
        }
        catch (JsonException ex)
        {
            throw new WebSocketException("The server sent a message that is not JSON.", ex);
        }
        return parsed as JsonObject
            ?? throw new WebSocketException("The server sent a message that is not a JSON object.");
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
        Fail(
            connection,
            new SocketUnavailableException(
                "The socket was disposed: it follows no more subscriptions."
            )
        );
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
            errors is { Count: > 0 }
            && errors[0] is JsonObject first
            && first["message"] is JsonValue message
            && message.TryGetValue<string>(out var text)
                ? text
                : "The server ended the subscription with an error."
        ) => Errors = errors ?? [];

    /// <summary>The errors as the server sent them.</summary>
    public JsonArray Errors { get; }
}

/// <summary>
/// The socket closed, before it was acknowledged or while subscribed, or the host refused to open it.
/// <see cref="CloseStatus"/> is the code the server closed it with, when it sent one: a 44xx code is the host refusing
/// the connection (4401 unauthorized, 4403 forbidden), and subscribing again will not change its answer. So is an
/// upgrade answered 401 or 403 (<see cref="HttpStatus"/>).
/// </summary>
public sealed class SocketClosedException : IOException
{
    /// <summary>Builds the exception.</summary>
    public SocketClosedException(string message, int? closeStatus, int? httpStatus = null)
        : base(message)
    {
        CloseStatus = closeStatus;
        HttpStatus = httpStatus;
    }

    /// <summary>The close code the server sent, or null when the socket closed without one.</summary>
    public int? CloseStatus { get; }

    /// <summary>The HTTP status the host refused the upgrade with, or null when the socket opened.</summary>
    public int? HttpStatus { get; }

    /// <summary>
    /// Whether the host refused the connection itself (a 4400 to 4499 close, or an upgrade answered 401 or 403), so a
    /// retry gets the same answer.
    /// </summary>
    public bool Refused => CloseStatus is >= 4400 and <= 4499 || HttpStatus is 401 or 403;
}

/// <summary>
/// There is no socket to follow a subscription over: the transport has none, or the socket was disposed. Subscribing
/// again gets the same answer.
/// </summary>
public sealed class SocketUnavailableException : Exception
{
    /// <summary>Builds the exception.</summary>
    public SocketUnavailableException(string message)
        : base(message) { }
}
