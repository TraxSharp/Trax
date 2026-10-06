namespace Trax.Api.Auth;

/// <summary>
/// Canonical Trax claim-type URNs used by every Trax authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class TraxAuthClaimTypes
{
    /// <summary>
    /// Claim type that carries the stable principal identifier across auth schemes, qualified by
    /// the scheme that authenticated it: <c>{scheme}:{id}</c> (see <see cref="TraxPrincipalId"/>).
    /// The id is the resolver's: the account name for API keys, <c>sub</c> for JWT schemes, the
    /// provider-assigned subject for OIDC and Cognito. So <c>sub = "abc"</c> from a JWT scheme
    /// named <c>Customer</c> is <c>Customer:abc</c>.
    /// </summary>
    public const string PrincipalId = "trax:principal-id";

    /// <summary>
    /// Claim type that carries the auth-scheme discriminator (<c>apikey</c>,
    /// <c>jwt</c>, <c>cognito</c>, etc.). Optional; set by schemes that support
    /// coexistence so audit sinks can distinguish principal sources.
    /// </summary>
    public const string PrincipalType = "trax:principal-type";

    /// <summary>
    /// Authorization policy name registered by every Trax auth extension that
    /// accepts any successfully authenticated Trax scheme. Use this when a
    /// route should admit multiple scheme types (e.g. API key OR JWT).
    /// </summary>
    public const string TraxAuthPolicy = "TraxAuthPolicy";
}
