using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Client;

namespace Trax.StateMachine.Client;

/// <summary>How a <see cref="GraphQLMachineServer"/> reaches its host.</summary>
public sealed record GraphQLMachineServerOptions
{
    /// <summary>
    /// The client the requests go through. Set its authentication on it, a default header or a delegating handler, as
    /// for any other <see cref="System.Net.Http.HttpClient"/>. It must not follow redirects: build it on a
    /// <see cref="SocketsHttpHandler"/> or <see cref="HttpClientHandler"/> with
    /// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> off, behind any delegating handlers
    /// (<c>new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })</c>). A client whose handler follows
    /// redirects, <c>new HttpClient()</c> among them, is refused when the server is built, and so is one on a handler of
    /// another kind unless <see cref="HandlerFollowsNoRedirects"/> is set. The check reads the handler at the end of the
    /// chain, so each delegating handler before it must pass every request on once, to the handler after it, and send
    /// none of its own: one that sends a request again, to the host a redirect names or anywhere else, does what the
    /// check cannot see. An answer from another host than <see cref="Endpoint"/>'s fails the request.
    /// </summary>
    public required HttpClient Http { get; init; }

    /// <summary>
    /// The host's GraphQL endpoint (<c>https://host/trax/graphql</c>), or one relative to <see cref="Http"/>'s base
    /// address. It must be <c>https</c> unless its host is loopback or <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Accepts an <c>http</c> endpoint on a host that is not loopback. Off by default: whatever authenticates the
    /// requests would travel in cleartext.
    /// </summary>
    public bool AllowInsecureTransport { get; init; }

    /// <summary>
    /// Says that <see cref="Http"/>'s handler follows no redirects, for a handler the server cannot check: one at the end
    /// of its chain that is neither a <see cref="SocketsHttpHandler"/> nor an <see cref="HttpClientHandler"/> (a test
    /// server's, a platform's own). Off by default, and such a client is refused. It changes nothing for a handler the
    /// server can check: one that follows redirects is refused either way.
    /// </summary>
    public bool HandlerFollowsNoRedirects { get; init; }

    /// <summary>
    /// The machine as the host's client manifest lists it, with its persisted documents, the push (<c>changed</c>)
    /// included. Every operation is sent by its document's id; a client never sends a document of its own.
    /// </summary>
    public required ClientManifestMachine Machine { get; init; }

    /// <summary>The socket the change stream is subscribed over.</summary>
    public required ISubscriptionSocket Socket { get; init; }

    /// <summary>
    /// Delays before subscribing again after a stream that ended without saying why (its host stopped, or the socket
    /// closed). The last repeats for as long as the stream is wanted; they start over once a stream has delivered a
    /// second snapshot (a reload push is not one), since every stream's first is the instance as it is. A socket the
    /// host refused (4401, 4403, or an upgrade answered 401 or 403), or a stream it refused with a GraphQL error coded
    /// <c>TRAX_AUTHORIZATION</c>, <c>AUTH_NOT_AUTHENTICATED</c> or <c>AUTH_NOT_AUTHORIZED</c>, is not subscribed
    /// again: it stops the controller as
    /// <c>UNAUTHENTICATED</c> or <c>FORBIDDEN</c>. Each is a step: the wait is drawn at random between half of it and
    /// the whole, so the instances a host dropped together do not all come back together.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResubscribeDelays { get; init; }

    // The clock the resubscribe delays and the request timeout run on, and where each wait is drawn from (a number from
    // 0 to 1); the system's clock and Random.Shared when null. A test pins them.
    internal TimeProvider? Time { get; init; }
    internal Func<double>? Random { get; init; }

    /// <summary>
    /// The codes of a stream error that ends the controller for good, beside the refusals above. A stream event or error
    /// whose first GraphQL error has one as its <c>extensions.code</c> is not subscribed again: it reaches the
    /// controller as <see cref="StreamMessage.Refused"/> with that code and the error's message.
    /// </summary>
    public IReadOnlyCollection<string>? TerminalStreamErrors { get; init; }

    /// <summary>
    /// The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one fails the request,
    /// which the controller reports as a transport failure, rather than being read into memory. It must be positive.
    /// </summary>
    public long MaxResponseSize { get; init; } = TraxHttpTransportOptions.DefaultMaxResponseSize;

    /// <summary>
    /// How long one request may take, from sending it to reading the last byte of its answer, before it fails as a
    /// transport failure; <see cref="GraphQLMachineServer.DefaultRequestTimeout"/> (30 seconds) by default. The
    /// <see cref="Http"/> client's own <see cref="HttpClient.Timeout"/> still applies, so the shorter of the two holds.
    /// <see cref="Timeout.InfiniteTimeSpan"/> leaves only the client's. Any other must be positive and at most
    /// <see cref="int.MaxValue"/> milliseconds (about 24.8 days), as <see cref="HttpClient.Timeout"/> must.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = GraphQLMachineServer.DefaultRequestTimeout;
}
