namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Constants for the Trax JWT bearer authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class JwtDefaults
{
    /// <summary>
    /// Name of the authentication scheme registered by <c>AddTraxJwtAuth</c>.
    /// </summary>
    public const string SchemeName = "TraxJwt";

    /// <summary>
    /// Authorization policy name registered by <c>AddTraxJwtAuth</c>. Requires
    /// an authenticated user authenticated via the <see cref="SchemeName"/> scheme.
    /// </summary>
    public const string PolicyName = "JwtPolicy";

    /// <summary>
    /// Discriminator written to <see cref="TraxAuthClaimTypes.PrincipalType"/>
    /// when the default JWT resolver builds a <see cref="TraxPrincipal"/>.
    /// </summary>
    public const string PrincipalType = "jwt";
}
