using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>How a <see cref="TraxHttpTransport"/> reaches its host.</summary>
public sealed record TraxHttpTransportOptions
{
    /// <summary>
    /// The client the requests go through. Set its authentication on it, a default header (<c>X-Api-Key</c>,
    /// <c>Authorization</c>) or a delegating handler, as for any other <see cref="System.Net.Http.HttpClient"/>. Its
    /// <see cref="HttpClient.Timeout"/> bounds each request, its body included. It must not follow redirects: build it
    /// on a <see cref="SocketsHttpHandler"/> or <see cref="HttpClientHandler"/> with
    /// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> off, behind any delegating handlers
    /// (<c>new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })</c>). A client whose handler follows
    /// redirects, <c>new HttpClient()</c> among them, is refused when the transport is built, and so is one on a handler
    /// of another kind unless <see cref="HandlerFollowsNoRedirects"/> is set. The check reads the handler at the end of
    /// the chain, so each delegating handler before it must pass every request on once, to the handler after it, and
    /// send none of its own: one that sends a request again, to the host a redirect names or anywhere else, does what
    /// the check cannot see. An answer from another host than <see cref="Endpoint"/>'s is a
    /// <see cref="TraxAnswer.Failure"/>.
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
    /// Says that <see cref="Http"/>'s handler follows no redirects, for a handler the transport cannot check: one at the
    /// end of its chain that is neither a <see cref="SocketsHttpHandler"/> nor an <see cref="HttpClientHandler"/> (a
    /// test server's, a platform's own). Off by default, and such a client is refused. It changes nothing for a handler
    /// the transport can check: one that follows redirects is refused either way.
    /// </summary>
    public bool HandlerFollowsNoRedirects { get; init; }

    /// <summary>
    /// The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one is a
    /// <see cref="TraxAnswer.Failure"/>, never read into memory. It must be positive.
    /// </summary>
    public long MaxResponseSize { get; init; } = DefaultMaxResponseSize;

    /// <summary>The response body limit when none is set: 16 MiB.</summary>
    public const long DefaultMaxResponseSize = 16 * 1024 * 1024;

    /// <summary>
    /// The socket subscriptions go over, usually a <see cref="GraphQLSubscriptionSocket"/>. Null fails every
    /// subscription at once, with a <see cref="SocketUnavailableException"/>.
    /// </summary>
    public ISubscriptionSocket? Socket { get; init; }

    /// <summary>
    /// Whether a document goes by its id rather than its text. By default every document that has an id does; a host
    /// that registered only some of them (or none, when it runs no persisted operations) names which.
    /// </summary>
    public Func<TraxDocument, bool>? ById { get; init; }
}
