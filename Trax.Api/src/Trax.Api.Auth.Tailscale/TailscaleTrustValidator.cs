using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Refuses to start a host that registers the tailnet sign-in scheme unless its deployment says it is reached only
/// through a Tailscale proxy it controls (<see cref="DeployExposure.ConfigurationKey"/> is
/// <see cref="DeployExposure.Tailnet"/>), and the proxy's secret is configured and at least
/// <see cref="TailscaleDefaults.MinimumProxySecretLength"/> characters. Either missing means the identity headers
/// could come from anyone who reaches the host, so the scheme does not start in any environment, Development
/// included.
/// <para>
/// It also refuses a default CORS policy that lets any origin in, by <c>AllowAnyOrigin()</c> or by an origin predicate
/// that admits an origin nobody serves, and an <see cref="TailscaleAuthenticationOptions.AllowedOrigins"/> value that
/// is not an origin. Serve names the tailnet user on every request their device sends, whatever page sent it, so the
/// login is an ambient credential that neither SameSite nor CORS's credential rules cover. The handler is what keeps a
/// page on another site from calling the hub as that user (<see cref="TailscaleOrigin"/>); the CORS check is an early
/// warning that the host meant to let other sites in.
/// </para>
/// </summary>
internal sealed class TailscaleTrustValidator(
    IConfiguration configuration,
    IOptionsMonitor<TailscaleAuthenticationOptions> options,
    IOptions<CorsOptions> cors
) : StartupGate
{
    /// <summary>An origin no host serves (<c>.invalid</c> is reserved), which a default policy must not admit.</summary>
    internal const string ForeignOrigin = "https://trax-tailnet-origin-check.invalid";

    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        var exposure = configuration[DeployExposure.ConfigurationKey];
        if (!string.Equals(exposure, DeployExposure.Tailnet, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "AddTraxTailscaleAuth() trusts Tailscale identity headers, and this host's exposure is "
                    + (exposure is null ? "not set" : $"'{exposure}'")
                    + $". It starts only where {DeployExposure.ConfigurationKey} is '{DeployExposure.Tailnet}', "
                    + "which trax deploy sets on a hub it exposes only through a Tailscale proxy of its own "
                    + "(\"exposure\": \"tailnet\"). A host reachable any other way would take a caller's word for "
                    + "who they are."
            );
        var secret = options.Get(TailscaleDefaults.SchemeName).ProxySecret;
        if (secret is null || secret.Length < TailscaleDefaults.MinimumProxySecretLength)
            throw new InvalidOperationException(
                "AddTraxTailscaleAuth() needs the proxy's secret, of at least "
                    + $"{TailscaleDefaults.MinimumProxySecretLength} characters, and "
                    + (secret is null ? "none is configured" : "the one configured is shorter")
                    + $". trax deploy sets it as {DeployExposure.ProxySecretVariable} on the hub and its proxy."
            );
        var notOrigins = options
            .Get(TailscaleDefaults.SchemeName)
            .AllowedOrigins.Where(o => !TailscaleOrigin.TryNormalize(o, out _))
            .ToList();
        if (notOrigins.Count > 0)
            throw new InvalidOperationException(
                "AddTraxTailscaleAuth() AllowedOrigins holds a value that is not an origin ("
                    + string.Join(", ", notOrigins.Select(o => $"'{o}'"))
                    + "). Give a scheme and host, with a port if it is not the default, and no path or query: "
                    + "https://app.example.com."
            );
        // An early warning only: the handler refuses a cross-site origin itself, whatever CORS allows.
        var policy = cors.Value.GetPolicy(cors.Value.DefaultPolicyName);
        if (policy is not null && (policy.AllowAnyOrigin || policy.IsOriginAllowed(ForeignOrigin)))
            throw new InvalidOperationException(
                "AddTraxTailscaleAuth() signs in whoever's device sent a request, and the default CORS policy "
                    + "lets any origin in ("
                    + (
                        policy.AllowAnyOrigin
                            ? "AllowAnyOrigin()"
                            : "its origin predicate admits one nobody serves"
                    )
                    + "). A page on any site a tailnet user opens could call this host as them and read the answer. "
                    + "Name the origins that may call it with WithOrigins(...)."
            );
        return Task.CompletedTask;
    }
}
