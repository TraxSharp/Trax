using Microsoft.IdentityModel.JsonWebTokens;

namespace Trax.Api.Auth.Jwt.Cognito;

/// <summary>
/// Builder helpers that wire <see cref="JwtBuilder"/> to validate tokens
/// from an Amazon Cognito user pool.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class CognitoJwtBuilderExtensions
{
    /// <summary>
    /// Configures the JWT builder for an Amazon Cognito user pool. Sets the
    /// authority to <c>https://cognito-idp.{region}.amazonaws.com/{userPoolId}</c>,
    /// the audience to <paramref name="clientId"/>, and an
    /// <c>AudienceValidator</c> that accepts both ID tokens (<c>aud</c> claim)
    /// and access tokens (<c>client_id</c> claim) depending on
    /// <paramref name="tokenUse"/>, and validates the <c>token_use</c> claim
    /// itself.
    /// </summary>
    /// <remarks>
    /// Where <paramref name="tokenUse"/> admits access tokens, a token carrying
    /// <c>client_id</c> but no <c>token_use</c> is rejected: Cognito always stamps
    /// <c>token_use</c> on an access token, so its absence means the token is not one.
    /// A token carrying neither claim is unaffected, which leaves a symmetric-key
    /// issuer configured on the same builder working as before.
    /// </remarks>
    /// <param name="builder">The JWT builder.</param>
    /// <param name="region">AWS region of the user pool, e.g. <c>us-east-1</c>.</param>
    /// <param name="userPoolId">User pool identifier, e.g. <c>us-east-1_AbCdEfGhI</c>.</param>
    /// <param name="clientId">App client id registered on the pool.</param>
    /// <param name="tokenUse">Which token shapes to accept. Defaults to both.</param>
    public static JwtBuilder UseCognito(
        this JwtBuilder builder,
        string region,
        string userPoolId,
        string clientId,
        CognitoTokenUse tokenUse = CognitoTokenUse.IdAndAccess
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPoolId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
        builder.UseAuthority(authority, clientId);

        builder.CustomizeTokenValidation(tvp =>
        {
            tvp.AudienceValidator = (audiences, token, _) =>
            {
                var acceptId =
                    tokenUse == CognitoTokenUse.Id || tokenUse == CognitoTokenUse.IdAndAccess;
                var acceptAccess =
                    tokenUse == CognitoTokenUse.Access || tokenUse == CognitoTokenUse.IdAndAccess;

                if (acceptId && audiences.Any(a => a == clientId))
                    return true;

                if (acceptAccess && token is JsonWebToken jwt)
                {
                    var claim = jwt.Claims.FirstOrDefault(c => c.Type == CognitoDefaults.ClientId);
                    if (claim is not null && claim.Value == clientId)
                        return true;
                }

                return false;
            };

            // Enforce token_use to match the configured selection. The
            // signature/issuer/audience checks above don't otherwise
            // distinguish id from access tokens.
            var existingLifetime = tvp.LifetimeValidator;
            tvp.LifetimeValidator = (notBefore, expires, token, parameters) =>
            {
                if (
                    existingLifetime is not null
                    && !existingLifetime(notBefore, expires, token, parameters)
                )
                    return false;

                if (token is JsonWebToken jwt)
                {
                    var use = jwt
                        .Claims.FirstOrDefault(c => c.Type == CognitoDefaults.TokenUse)
                        ?.Value;

                    if (
                        tokenUse == CognitoTokenUse.Id
                        && use is not null
                        && use != CognitoDefaults.TokenUseId
                    )
                        return false;

                    if (
                        tokenUse == CognitoTokenUse.Access
                        && use is not null
                        && use != CognitoDefaults.TokenUseAccess
                    )
                        return false;

                    // A client_id claim is what makes a token the access shape, and Cognito always
                    // stamps token_use on one. Its absence therefore means the token is not
                    // something Cognito issued in that shape, so admitting it on the strength of
                    // client_id alone — which is exactly what the AudienceValidator's access branch
                    // above does — would accept a token nothing has vouched for. Fail closed.
                    //
                    // Scoped to client_id on purpose: a token carrying neither claim is not being
                    // presented as an access token at all, and the internal HS256 issuer mints
                    // precisely that, so requiring token_use of it would break a path that has
                    // nothing to do with Cognito.
                    var acceptsAccessShape =
                        tokenUse == CognitoTokenUse.Access
                        || tokenUse == CognitoTokenUse.IdAndAccess;

                    if (
                        acceptsAccessShape
                        && use is null
                        && jwt.Claims.Any(c => c.Type == CognitoDefaults.ClientId)
                    )
                        return false;
                }

                // Fall back to the default lifetime validator (exp/nbf check).
                if (expires is null)
                    return false;
                var now = DateTime.UtcNow;
                var skew = parameters.ClockSkew;
                if (notBefore is { } nbf && nbf > now + skew)
                    return false;
                return expires.Value > now - skew;
            };
        });

        return builder;
    }
}
