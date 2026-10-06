using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.Auth.Oidc;

/// <summary>
/// Fluent configuration for the Trax OIDC scheme. Wires both the challenge
/// handler (<see cref="OidcDefaults.SchemeName"/>) and the session cookie
/// (<see cref="OidcDefaults.CookieSchemeName"/>) that authenticates subsequent
/// requests.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// OIDC is a browser protocol. The challenge scheme issues a redirect to the
/// identity provider; on callback, the handler validates the id-token and
/// signs the user into the cookie scheme. Non-browser API clients should use
/// <c>Trax.Api.Auth.Jwt</c> instead.
/// </para>
/// </remarks>
public sealed class OidcBuilder
{
    internal string? Authority { get; private set; }
    internal string? ClientId { get; private set; }
    internal string? ClientSecret { get; private set; }
    internal string ResponseType { get; private set; } = "code";
    internal bool UsePkce { get; private set; } = true;
    internal bool SaveTokens { get; private set; } = true;
    internal string CallbackPath { get; private set; } = OidcDefaults.CallbackPath;
    internal string SignedOutCallbackPath { get; private set; } =
        OidcDefaults.SignedOutCallbackPath;
    internal bool RequireHttpsMetadata { get; private set; } = true;
    internal List<string> Scopes { get; } = ["openid", "profile"];
    internal Action<OpenIdConnectOptions>? OidcOptionsCustomizer { get; private set; }
    internal Action<CookieAuthenticationOptions>? CookieOptionsCustomizer { get; private set; }

    /// <summary>
    /// Configures the OIDC provider authority (must support the OIDC discovery
    /// document at <c>{authority}/.well-known/openid-configuration</c>) and
    /// the client id registered with the provider.
    /// </summary>
    public OidcBuilder UseAuthority(string authority, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        Authority = authority;
        ClientId = clientId;
        return this;
    }

    /// <summary>
    /// Sets the client secret for confidential clients. Omit for public
    /// clients (SPA, native app) that use PKCE alone.
    /// </summary>
    public OidcBuilder WithClientSecret(string clientSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ClientSecret = clientSecret;
        return this;
    }

    /// <summary>
    /// Adds scopes to request beyond <c>openid</c> and <c>profile</c> (which
    /// are registered by default).
    /// </summary>
    public OidcBuilder AddScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!Scopes.Contains(scope, StringComparer.Ordinal))
            Scopes.Add(scope);
        return this;
    }

    /// <summary>
    /// Overrides the callback path (default <c>/signin-oidc</c>).
    /// </summary>
    public OidcBuilder WithCallbackPath(string callbackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);
        CallbackPath = callbackPath;
        return this;
    }

    /// <summary>
    /// Overrides the signed-out callback path (default <c>/signout-callback-oidc</c>).
    /// </summary>
    public OidcBuilder WithSignedOutCallbackPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SignedOutCallbackPath = path;
        return this;
    }

    /// <summary>
    /// Permits non-HTTPS authority metadata. Development only.
    /// </summary>
    public OidcBuilder AllowHttpMetadata()
    {
        RequireHttpsMetadata = false;
        return this;
    }

    /// <summary>
    /// Disables storing the access and id tokens on the session cookie. By
    /// default they are saved so downstream code can call the provider's
    /// userinfo endpoint or forward tokens.
    /// </summary>
    public OidcBuilder DoNotSaveTokens()
    {
        SaveTokens = false;
        return this;
    }

    /// <summary>
    /// Hook for raw <see cref="OpenIdConnectOptions"/> access. Runs after Trax
    /// has wired <c>OnTokenValidated</c> to the principal resolver, so do not
    /// overwrite the events collection wholesale.
    /// </summary>
    public OidcBuilder CustomizeOidcOptions(Action<OpenIdConnectOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        OidcOptionsCustomizer = configure;
        return this;
    }

    /// <summary>
    /// Hook for the session cookie options (expiration, SameSite, domain, etc.).
    /// </summary>
    public OidcBuilder CustomizeCookieOptions(Action<CookieAuthenticationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        CookieOptionsCustomizer = configure;
        return this;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Authority) || string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException(
                "AddTraxOidcAuth(oidc => ...) requires UseAuthority(authority, clientId)."
            );

        if (!PathString.FromUriComponent(CallbackPath).HasValue)
            throw new InvalidOperationException(
                $"OIDC callback path '{CallbackPath}' is not a valid absolute URL path."
            );
    }
}
