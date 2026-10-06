using Trax.Effect.Broadcaster.SignalR.Services;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;

public partial class TraxTrainEventHubOptions
{
    private List<string>? _allowedOrigins;

    /// <summary>
    /// Sets the browser origins, besides the hub's own, from which a request to the hub (its
    /// negotiate call, a WebSocket upgrade, or a Server-Sent Events or long-polling request) is
    /// accepted. Calling it again adds origins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request carrying an <c>Origin</c> header is accepted when that origin is on the hub's own
    /// host, or is allowed; otherwise it is refused with <c>403</c>, whatever its credentials,
    /// before the connection is made. A request with no <c>Origin</c> header, which is what a
    /// non-browser client sends, is accepted, and is left to the authorization posture.
    /// </para>
    /// <para>
    /// Without this call the allowed origins are those of the host's CORS default policy
    /// (<c>AddCors(o =&gt; o.AddDefaultPolicy(...))</c>), including <c>AllowAnyOrigin()</c>, and
    /// with no default policy only the hub's own origin is. With it, exactly these origins are
    /// allowed and the CORS default policy is not consulted, so calling it with no arguments
    /// allows the hub's own origin only.
    /// </para>
    /// <para>
    /// Each value is an origin: a scheme and a host, with a port when it is not the scheme's
    /// default, and nothing else (<c>https://app.example.com</c>). Matching ignores case and a
    /// spelled-out default port. The scheme of the hub's own origin is not compared, because TLS
    /// is commonly terminated in front of the app.
    /// </para>
    /// </remarks>
    /// <param name="origins">The origins to allow.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentException">A value is not an absolute http(s) origin.</exception>
    public TraxTrainEventHubOptions AllowOrigins(params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        _allowedOrigins ??= [];
        foreach (var origin in origins)
            _allowedOrigins.Add(
                HubOriginPolicy.TryNormalize(origin, out var normalized)
                    ? normalized
                    : throw new ArgumentException(
                        $"'{origin}' is not an origin. Pass a scheme and host, with a port if it "
                            + "is not the default, and no path or query: https://app.example.com.",
                        nameof(origins)
                    )
            );

        return this;
    }
}
