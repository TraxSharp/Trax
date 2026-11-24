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
/// </summary>
internal sealed class TailscaleTrustValidator(
    IConfiguration configuration,
    IOptionsMonitor<TailscaleAuthenticationOptions> options
) : StartupGate
{
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
        return Task.CompletedTask;
    }
}
