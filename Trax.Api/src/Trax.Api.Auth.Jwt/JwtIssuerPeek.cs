using Microsoft.IdentityModel.JsonWebTokens;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Reads the <c>iss</c> claim of a JWT without validating its signature.
/// Used by the dispatcher to pick which validation scheme should run; the
/// chosen scheme then performs the full signature and claim validation.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// The returned issuer is untrusted: it has not been validated against the
/// token's signature. Callers must use it only to dispatch to a validating
/// scheme, never as the authenticated issuer.
/// </para>
/// </remarks>
internal static class JwtIssuerPeek
{
    /// <summary>
    /// Attempts to extract the <c>iss</c> claim from <paramref name="token"/>.
    /// Returns <c>null</c> for empty strings, malformed tokens, and tokens
    /// without an <c>iss</c> claim.
    /// </summary>
    public static string? TryReadIssuer(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var jwt = new JsonWebToken(token);
            var issuer = jwt.Issuer;
            return string.IsNullOrWhiteSpace(issuer) ? null : issuer;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Strips the <c>Bearer</c> prefix from an Authorization header value,
    /// returning the raw token or <c>null</c> if the header does not carry
    /// a bearer credential.
    /// </summary>
    public static string? TryReadBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorizationHeader[prefix.Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
