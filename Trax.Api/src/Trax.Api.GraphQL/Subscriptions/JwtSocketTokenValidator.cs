using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Validates a JWT carried in a subscription <c>connection_init</c> payload
/// against a scheme's <see cref="JwtBearerOptions"/>, resolving JWKS signing
/// keys from the scheme's OIDC discovery document when the options carry no
/// static key. Shared by the single-scheme and dispatcher socket interceptors.
/// </summary>
internal static class JwtSocketTokenValidator
{
    public static async Task<TokenValidationResult> ValidateAsync(
        string token,
        JwtBearerOptions options,
        CancellationToken cancellationToken
    )
    {
        var parameters = options.TokenValidationParameters.Clone();

        var hasStaticKey =
            parameters.IssuerSigningKey is not null
            || (parameters.IssuerSigningKeys?.Any() ?? false);

        // Authority/JWKS schemes (Cognito, Google, any OIDC provider) carry no
        // signing keys on TokenValidationParameters until the discovery document
        // is fetched. The HTTP JwtBearerHandler does this through its
        // ConfigurationManager; the socket path must do the same or every
        // JWKS-backed token is rejected for want of a key.
        if (!hasStaticKey && options.ConfigurationManager is not null)
        {
            OpenIdConnectConfiguration configuration =
                await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);

            parameters.IssuerSigningKeys = (
                parameters.IssuerSigningKeys ?? Enumerable.Empty<SecurityKey>()
            ).Concat(configuration.SigningKeys);

            // The bearer handler validates against the issuer advertised by the
            // discovery document when no explicit issuer was configured on the
            // options. Mirror that so issuer validation stays enabled.
            if (string.IsNullOrEmpty(parameters.ValidIssuer) && parameters.ValidIssuers is null)
                parameters.ValidIssuer = configuration.Issuer;
        }

        var handler = new JsonWebTokenHandler();
        return await handler.ValidateTokenAsync(token, parameters);
    }
}
