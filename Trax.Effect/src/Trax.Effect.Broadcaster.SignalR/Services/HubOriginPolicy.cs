using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// Decides whether a request to the train-event hub may proceed, from its <c>Origin</c> header,
/// as Trax.Api decides it for a WebSocket upgrade to its GraphQL endpoint.
/// </summary>
internal static class HubOriginPolicy
{
    /// <summary>
    /// True when the request may proceed: it carries no <c>Origin</c>, or the origin is on the
    /// request's own host, or it is allowed. <paramref name="allowedOrigins"/> is the normalized
    /// explicit list, or <c>null</c> to use the host's CORS default policy.
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
        if (policy is null)
            return false;

        return policy.AllowAnyOrigin
            || policy.IsOriginAllowed(origin)
            || policy.IsOriginAllowed(normalized);
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
