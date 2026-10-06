using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Refuses to start a host outside Development when any JWT bearer scheme validates tokens with a
/// signing key that carries <see cref="JwtBuilder.DemoKeyMarker"/>, the marker the Trax templates
/// and samples put on their demo signing keys. Registered by every <c>AddTraxJwtAuth</c> call. The
/// API-key counterpart is <c>DemoApiKeyEnvironmentValidator</c>.
/// </summary>
/// <remarks>
/// It reads each scheme's final <see cref="JwtBearerOptions"/>, after every configure and
/// post-configure step, so a key is caught wherever it was set: <see cref="JwtBuilder.UseSymmetricKey(string, string, byte[])"/>,
/// <see cref="JwtBuilder.UseSigningKey"/>, <see cref="JwtBuilder.CustomizeTokenValidation"/>,
/// <see cref="JwtBuilder.CustomizeBearerOptions"/>, or a bearer scheme the host registered itself.
/// Both <c>IssuerSigningKey</c> and <c>IssuerSigningKeys</c> are read. Keys produced at
/// validation time by an <c>IssuerSigningKeyResolver</c> or fetched from an authority's JWKS do not
/// exist at startup, so they are not checked.
/// </remarks>
internal sealed class DemoJwtKeyEnvironmentValidator(
    IHostEnvironment environment,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitor<JwtBearerOptions> bearerOptions
) : StartupGate
{
    protected override async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
            return;

        foreach (var scheme in await schemes.GetAllSchemesAsync())
        {
            if (!typeof(JwtBearerHandler).IsAssignableFrom(scheme.HandlerType))
                continue;

            var parameters = bearerOptions.Get(scheme.Name).TokenValidationParameters;
            if (
                JwtBuilder.IsDemoKey(parameters.IssuerSigningKey)
                || parameters.IssuerSigningKeys?.Any(JwtBuilder.IsDemoKey) == true
            )
                throw new InvalidOperationException(
                    $"The JWT bearer scheme '{scheme.Name}' validates tokens with a signing key "
                        + $"containing '{JwtBuilder.DemoKeyMarker}', which marks a published demo "
                        + $"key, and the environment is '{environment.EnvironmentName}'. Such keys "
                        + "start only in Development. Load the signing key from a secret store, "
                        + "or use UseAuthority(...) for an identity provider, outside Development."
                );
        }
    }
}
