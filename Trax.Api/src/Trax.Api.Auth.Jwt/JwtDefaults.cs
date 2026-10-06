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
    /// Default name of the authentication scheme registered by
    /// <c>AddTraxJwtAuth</c> when no scheme name is supplied. Hosts that
    /// register multiple schemes pass explicit names to the named-scheme
    /// overloads instead of relying on this constant.
    /// </summary>
    public const string SchemeName = "TraxJwt";

    /// <summary>
    /// Default authorization policy name registered by <c>AddTraxJwtAuth</c>
    /// when no scheme name is supplied. The policy requires an authenticated
    /// user via <see cref="SchemeName"/>.
    /// </summary>
    public const string PolicyName = "JwtPolicy";

    /// <summary>
    /// Discriminator written to <see cref="TraxAuthClaimTypes.PrincipalType"/>
    /// when the default JWT resolver builds a <see cref="TraxPrincipal"/>.
    /// </summary>
    public const string PrincipalType = "jwt";

    /// <summary>
    /// Default name of the policy scheme registered by
    /// <c>AddTraxJwtDispatcher</c>. The dispatcher inspects the inbound
    /// Bearer token's <c>iss</c> claim and forwards authentication to the
    /// matching JWT scheme. Hosts can override this via the dispatcher
    /// builder.
    /// </summary>
    public const string DispatcherSchemeName = "TraxJwtDispatcher";

    /// <summary>
    /// Internal name of the rejection scheme registered alongside the
    /// dispatcher. It returns <see cref="Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail(string)"/>
    /// for every request, used as the dispatcher's fall-through when an
    /// inbound token's issuer matches no registered scheme.
    /// </summary>
    public const string RejectSchemeName = "TraxJwtReject";

    /// <summary>
    /// Suffix appended to a scheme name to derive its per-scheme policy name
    /// when a named scheme is registered. For example, the scheme
    /// <c>cognito</c> produces the policy name <c>cognito-JwtPolicy</c>.
    /// </summary>
    internal const string PolicyNameSuffix = "-JwtPolicy";
}
