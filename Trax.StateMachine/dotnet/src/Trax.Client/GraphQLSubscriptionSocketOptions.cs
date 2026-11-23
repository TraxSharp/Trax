using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

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
    /// closing the socket is found this way. <see cref="Timeout.InfiniteTimeSpan"/> waits forever. Any other must be
    /// positive and at most <see cref="int.MaxValue"/> milliseconds.
    /// </summary>
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a connection may take to be acknowledged, its <c>connection_init</c> included, before it counts as
    /// failed; and how long a close waits for the host's answer before the connection is dropped. 10 seconds by default;
    /// <see cref="Timeout.InfiniteTimeSpan"/> waits forever. Any other must be positive and at most
    /// <see cref="int.MaxValue"/> milliseconds (about 24.8 days).
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The largest message the server may send, in bytes; 16 MiB by default. A larger one fails the connection, and
    /// every subscription over it, rather than being read into memory. It must be positive.
    /// </summary>
    public int MaxMessageSize { get; init; } = 16 * 1024 * 1024;
}
