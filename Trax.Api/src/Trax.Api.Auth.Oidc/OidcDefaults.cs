namespace Trax.Api.Auth.Oidc;

/// <summary>
/// Constants for the Trax OpenID Connect authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class OidcDefaults
{
    /// <summary>
    /// Challenge scheme name. A challenge against this scheme redirects the
    /// browser to the identity provider for sign-in.
    /// </summary>
    public const string SchemeName = "TraxOidc";

    /// <summary>
    /// Session cookie scheme name. After a successful callback, identity is
    /// stored in this cookie; subsequent requests authenticate against it.
    /// This is the scheme that GraphQL and MVC endpoints authorize against.
    /// </summary>
    public const string CookieSchemeName = "TraxOidc.Cookie";

    /// <summary>
    /// Authorization policy name registered by <c>AddTraxOidcAuth</c>. Requires
    /// an authenticated user authenticated via the <see cref="CookieSchemeName"/>.
    /// </summary>
    public const string PolicyName = "OidcPolicy";

    /// <summary>
    /// Default callback path mounted by the OIDC handler.
    /// </summary>
    public const string CallbackPath = "/signin-oidc";

    /// <summary>
    /// Default sign-out callback path.
    /// </summary>
    public const string SignedOutCallbackPath = "/signout-callback-oidc";

    /// <summary>
    /// Discriminator written to <see cref="TraxAuthClaimTypes.PrincipalType"/>
    /// when the default OIDC resolver builds a <see cref="TraxPrincipal"/>.
    /// </summary>
    public const string PrincipalType = "oidc";
}
