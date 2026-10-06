using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Decides whether a WebSocket upgrade to the Trax GraphQL endpoint may proceed, from its
/// <c>Origin</c> header. See <c>docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md</c>.
/// </summary>
internal static class SocketOriginPolicy
{
    /// <summary>
    /// True when the upgrade may proceed: it carries no <c>Origin</c>, or the origin is on the
    /// request's own host, or it is allowed. <paramref name="allowedOrigins"/> is the normalized
    /// explicit list, or <c>null</c> to use the host's CORS default policy, whose
    /// <c>AllowAnyOrigin()</c> admits no origin besides the endpoint's own.
    /// </summary>
    public static bool IsAllowed(HttpContext context, IReadOnlyList<string>? allowedOrigins)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true;

        if (!TryNormalize(origin, out var normalized))
            return false;

        if (IsSameHost(normalized, context.Request.Host))
            return true;

        if (allowedOrigins is not null)
            return allowedOrigins.Contains(normalized, StringComparer.Ordinal);

        var cors = context.RequestServices.GetService<IOptions<CorsOptions>>()?.Value;
        var policy = cors?.GetPolicy(cors.DefaultPolicyName);

        // A browser attaches its cookies to every WebSocket upgrade, so a socket is always a
        // credentialed request. CORS never lets AllowAnyOrigin() carry credentials, and neither
        // does the socket: only an origin the policy names, or its origin predicate admits, is
        // allowed.
        if (policy is null || policy.AllowAnyOrigin)
            return false;

        return policy.IsOriginAllowed(origin) || policy.IsOriginAllowed(normalized);
    }

    /// <summary>
    /// Parses <paramref name="origin"/> as an http(s) origin (scheme, host and optional port, no
    /// path, query, fragment or user info) and returns it lowercased with any default port
    /// dropped.
    /// </summary>
    public static bool TryNormalize(string? origin, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (
            uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0
        )
            return false;

        normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// The origin is on the host the request was addressed to. The scheme is not compared: TLS
    /// is commonly terminated in front of the app, so an https page reaches it as http.
    /// </summary>
    private static bool IsSameHost(string normalizedOrigin, HostString requestHost)
    {
        if (!requestHost.HasValue)
            return false;

        var scheme = normalizedOrigin[..normalizedOrigin.IndexOf(':')];
        return TryNormalize($"{scheme}://{requestHost.Value}", out var own)
            && string.Equals(own, normalizedOrigin, StringComparison.Ordinal);
    }
}
