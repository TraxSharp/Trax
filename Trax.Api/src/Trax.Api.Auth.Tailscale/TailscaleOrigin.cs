using Microsoft.AspNetCore.Http;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Whether a request may carry its tailnet login, by the page that sent it. Serve adds the login to every request the
/// user's device sends, whatever site started it, so the scheme reads it only from a request a browser says came from
/// the host's own origin or one <see cref="TailscaleAuthenticationOptions.AllowedOrigins"/> names, and from a request
/// no browser page started. CORS plays no part: a host's CORS policy, whatever form it takes, cannot widen this.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Origin</c> present: allowed when it is the request's own host (any scheme, since TLS ends at the proxy) or
/// an allowed origin. An opaque origin (the text "null"), or anything that is not an http(s) origin, is not.</item>
/// <item>No <c>Origin</c>, and <c>Sec-Fetch-Site</c> <c>cross-site</c> or <c>same-site</c> on a request that is not a
/// navigation (<c>Sec-Fetch-Mode</c> is not <c>navigate</c>): not allowed. That is a subresource another page
/// embedded.</item>
/// <item>Otherwise allowed: a non-browser caller, Serve's health checks, a same-origin <c>GET</c>, and the user's own
/// navigation.</item>
/// </list>
/// <c>Referer</c> is not read: a page can drop it, so its absence says nothing, and <c>Origin</c> carries what it
/// would.
/// </remarks>
internal static class TailscaleOrigin
{
    /// <summary>Whether <paramref name="request"/> may be signed in as its tailnet login.</summary>
    public static bool Allowed(HttpRequest request, IEnumerable<string> allowedOrigins)
    {
        var headers = request.Headers;
        if (headers.Origin.Count > 1)
            return false;
        var origin = headers.Origin.ToString();
        if (origin.Length == 0)
            return !Embedded(request);
        if (!TryNormalize(origin, out var normalized))
            return false;
        return SameHost(normalized, request.Host)
            || allowedOrigins.Any(a =>
                TryNormalize(a, out var allowed)
                && string.Equals(allowed, normalized, StringComparison.Ordinal)
            );
    }

    /// <summary>A request without <c>Origin</c> that a browser sent for a page on another origin, not a navigation.</summary>
    private static bool Embedded(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (
            !string.Equals(site, "cross-site", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(site, "same-site", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        return !string.Equals(
            request.Headers["Sec-Fetch-Mode"].ToString(),
            "navigate",
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// Parses <paramref name="origin"/> as an http(s) origin (scheme, host and optional port, nothing else) and returns
    /// it lowercased with a default port dropped.
    /// </summary>
    public static bool TryNormalize(string? origin, out string normalized)
    {
        normalized = string.Empty;
        if (
            string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        )
            return false;
        if (
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        )
            return false;
        if (
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0
        )
            return false;
        normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }

    /// <summary>The origin is on the host the request was addressed to, in either scheme.</summary>
    private static bool SameHost(string normalizedOrigin, HostString requestHost)
    {
        if (!requestHost.HasValue)
            return false;
        var scheme = normalizedOrigin[..normalizedOrigin.IndexOf(':', StringComparison.Ordinal)];
        return TryNormalize($"{scheme}://{requestHost.Value}", out var own)
            && string.Equals(own, normalizedOrigin, StringComparison.Ordinal);
    }
}
