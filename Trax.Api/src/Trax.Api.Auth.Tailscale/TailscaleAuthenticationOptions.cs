using Microsoft.AspNetCore.Authentication;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Options for the Trax tailnet sign-in scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-tailscale-auth">SDK reference</seealso>
public sealed class TailscaleAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The secret the proxy in front of the host sends in <see cref="DeployExposure.ProxySecretHeader"/>. When it is
    /// not set here it is read from configuration under <see cref="DeployExposure.ProxySecretKey"/>, where
    /// <c>trax deploy</c> puts it. A request without it, or with another value, is anonymous. The host refuses to start
    /// when it is shorter than <see cref="TailscaleDefaults.MinimumProxySecretLength"/>.
    /// </summary>
    public string? ProxySecret { get; set; }
}
