namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// The origins configured through <see cref="AllowSocketOrigins"/>, normalized, or
    /// <c>null</c> when the method was not called and the CORS default policy applies.
    /// </summary>
    internal IReadOnlyList<string>? SocketAllowedOrigins { get; private set; }

    /// <summary>
    /// Sets the browser origins, besides the endpoint's own, from which a WebSocket upgrade to
    /// the Trax GraphQL schema is accepted, whether it is mapped by <c>UseTraxGraphQL()</c> or by
    /// the host's own <c>MapGraphQL(path, "trax")</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An upgrade carrying an <c>Origin</c> header is accepted when that origin is on the
    /// endpoint's own host, or is allowed; otherwise it is refused with <c>403</c> before the
    /// handshake completes. An upgrade with no <c>Origin</c> header, which is what a non-browser
    /// client sends, is accepted. Plain HTTP requests are not affected; CORS governs those.
    /// </para>
    /// <para>
    /// Without this call the allowed origins are those of the host's CORS default policy
    /// (<c>AddCors(o =&gt; o.AddDefaultPolicy(...))</c>), including <c>AllowAnyOrigin()</c>. With
    /// it, exactly these origins are allowed and the CORS default policy is not consulted, so
    /// calling it with no arguments allows the endpoint's own origin only.
    /// </para>
    /// <para>
    /// Each value is an origin: a scheme and a host, with a port when it is not the scheme's
    /// default, and nothing else (<c>https://app.example.com</c>). Matching ignores case and a
    /// spelled-out default port.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A value is not an absolute http(s) origin.</exception>
    public TraxGraphQLBuilder AllowSocketOrigins(params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        var normalized = new List<string>(origins.Length);
        foreach (var origin in origins)
        {
            normalized.Add(
                Subscriptions.SocketOriginPolicy.TryNormalize(origin, out var value)
                    ? value
                    : throw new ArgumentException(
                        $"'{origin}' is not an origin. Pass a scheme and host, with a port if it "
                            + "is not the default, and no path or query: https://app.example.com.",
                        nameof(origins)
                    )
            );
        }

        SocketAllowedOrigins = normalized;
        return this;
    }
}
