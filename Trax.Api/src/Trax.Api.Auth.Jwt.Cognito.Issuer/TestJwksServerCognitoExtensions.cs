using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt.Testing;

namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Convenience extensions that pair a <see cref="TestJwksServer"/> with a
/// <see cref="CognitoTokenIssuer"/>. The extension lives in the issuer
/// package (not the testing package) so consumers of
/// <c>Trax.Api.Auth.Jwt.Testing</c> aren't transitively pulled into Cognito
/// issuance.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class TestJwksServerCognitoExtensions
{
    /// <summary>
    /// Build a <see cref="CognitoTokenIssuer"/> wired to this server's
    /// current signing key and issuer URL. Tokens minted via the returned
    /// issuer validate against this server's JWKS.
    /// </summary>
    /// <param name="server">The JWKS server.</param>
    /// <param name="clock">
    /// Optional deterministic clock for tests. Defaults to
    /// <see cref="TimeProvider.System"/>.
    /// </param>
    public static CognitoTokenIssuer CreateCognitoIssuer(
        this TestJwksServer server,
        TimeProvider? clock = null
    )
    {
        ArgumentNullException.ThrowIfNull(server);
        return new CognitoTokenIssuer(server.Issuer, server.SigningKey, clock);
    }
}
