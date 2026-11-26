using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// ASP.NET Core authentication handler for the Trax tailnet sign-in scheme. It reads Tailscale serve's identity
/// headers only from a request that carries the proxy's secret in <see cref="DeployExposure.ProxySecretHeader"/>, and
/// never looks at the address the request came from, so neither a forwarded-for header nor a sender on a trusted
/// network can make a request look like the proxy's.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Return semantics:
/// <list type="bullet">
/// <item>No proxy secret, more than one, or a value that does not match: <see cref="AuthenticateResult.NoResult"/>, whatever identity headers the request carries. A wrong value is logged at Warning, without the value.</item>
/// <item>The secret, and a request a page on another site sent (<see cref="TailscaleOrigin"/>): <see cref="AuthenticateResult.NoResult"/>, whatever the host's CORS policy allows.</item>
/// <item>The secret, and a Funnel request or no login (a tagged device, or the proxy's own host): <see cref="AuthenticateResult.NoResult"/>.</item>
/// <item>The secret, and the login header more than once, or one value holding a comma, or one that does not decode: <see cref="AuthenticateResult.Fail(string)"/>.</item>
/// <item>The secret, and a login the builder does not admit: <see cref="AuthenticateResult.Fail(string)"/>.</item>
/// <item>The secret, and an admitted login: <see cref="AuthenticateResult.Success"/> with the login's roles.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class TailscaleAuthHandler(
    IOptionsMonitor<TailscaleAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TailscaleLogins logins
) : AuthenticationHandler<TailscaleAuthenticationOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(Read());

    private AuthenticateResult Read()
    {
        if (!FromProxy() || !FromAllowedPage())
            return AuthenticateResult.NoResult();

        var headers = Request.Headers;
        if (headers.ContainsKey(TailscaleDefaults.FunnelHeader))
            return AuthenticateResult.NoResult();
        if (!headers.TryGetValue(TailscaleDefaults.LoginHeader, out var values))
            return AuthenticateResult.NoResult();
        if (values.Count > 1)
            return AuthenticateResult.Fail("Multiple tailnet logins presented.");
        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return AuthenticateResult.NoResult();
        // A proxy that joins duplicate headers into one comma-separated value (RFC 9110 5.3) hides
        // the second login from the count above.
        if (raw.Contains(',', StringComparison.Ordinal))
            return AuthenticateResult.Fail("Ambiguous tailnet login header.");
        var login = TailscaleHeaderValue.Decode(raw);
        if (
            string.IsNullOrWhiteSpace(login)
            || login.Contains(',', StringComparison.Ordinal)
            || login.Trim().Length != login.Length
        )
            return AuthenticateResult.Fail("Unreadable tailnet login header.");

        var roles = logins.RolesOf(login);
        if (roles is null)
        {
            Logger.LogWarning(
                "Tailnet login {Login} is not admitted by AddTraxTailscaleAuth; the request is not signed in.",
                login
            );
            return AuthenticateResult.Fail("Tailnet login not admitted.");
        }

        var principal = new TraxPrincipal(
            login,
            Single(TailscaleDefaults.NameHeader) is { } name
            && TailscaleHeaderValue.Decode(name) is { } decoded
            && !string.IsNullOrWhiteSpace(decoded)
                ? decoded
                : login,
            [.. roles],
            Picture() is { } picture
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TailscaleDefaults.ProfilePictureClaim] = picture,
                }
                : null,
            TailscaleDefaults.PrincipalType
        );
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal.ToClaimsPrincipal(Scheme.Name), Scheme.Name)
        );
    }

    /// <summary>
    /// Whether the request carries the proxy's secret, once. Compared through <see cref="TailscaleProxySecret"/>, in
    /// constant time.
    /// </summary>
    private bool FromProxy()
    {
        if (!Request.Headers.TryGetValue(DeployExposure.ProxySecretHeader, out var presented))
            return false;
        var secret = Options.ProxySecret;
        if (string.IsNullOrEmpty(secret))
            return false;
        if (presented.Count == 1 && new TailscaleProxySecret(secret).Matches(presented.ToString()))
            return true;
        Logger.LogWarning(
            "A request carried {Header} with a value that is not the proxy's; its tailnet identity headers were ignored.",
            DeployExposure.ProxySecretHeader
        );
        return false;
    }

    /// <summary>Whether the page that sent the request, if any, may call the host as the user (<see cref="TailscaleOrigin"/>).</summary>
    private bool FromAllowedPage()
    {
        if (TailscaleOrigin.Allowed(Request, Options.AllowedOrigins))
            return true;
        Logger.LogInformation(
            "A request from origin {Origin} is not signed in by tailnet identity: another site's page sent it.",
            Request.Headers.Origin.ToString()
        );
        return false;
    }

    private string? Single(string header) =>
        Request.Headers.TryGetValue(header, out var values) && values.Count == 1
            ? values.ToString()
            : null;

    /// <summary>The profile picture's address, when it is one absolute https or http address.</summary>
    private string? Picture() =>
        Single(TailscaleDefaults.ProfilePictureHeader) is { Length: > 0 } value
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
        )
            ? uri.AbsoluteUri
            : null;
}
