using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Constants for the Trax tailnet sign-in scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-tailscale-auth">SDK reference</seealso>
public static class TailscaleDefaults
{
    /// <summary>Name of the authentication scheme registered by <c>AddTraxTailscaleAuth</c>.</summary>
    public const string SchemeName = DeployExposure.TailscaleScheme;

    /// <summary>
    /// Authorization policy registered by <c>AddTraxTailscaleAuth</c>, satisfied only by a caller this scheme
    /// authenticated.
    /// </summary>
    public const string PolicyName = "TailscalePolicy";

    /// <summary>The header Tailscale serve fills with the caller's login name (<c>alice@example.com</c>).</summary>
    public const string LoginHeader = "Tailscale-User-Login";

    /// <summary>The header Tailscale serve fills with the caller's display name.</summary>
    public const string NameHeader = "Tailscale-User-Name";

    /// <summary>The header Tailscale serve fills with the caller's profile picture address, when there is one.</summary>
    public const string ProfilePictureHeader = "Tailscale-User-Profile-Pic";

    /// <summary>The header Tailscale serve sets on a request that came through Funnel, from outside the tailnet.</summary>
    public const string FunnelHeader = "Tailscale-Funnel-Request";

    /// <summary>The claim the profile picture address is carried in.</summary>
    public const string ProfilePictureClaim = "picture";

    /// <summary>The <c>PrincipalType</c> of a principal this scheme authenticates.</summary>
    public const string PrincipalType = "tailscale";

    /// <summary>The fewest characters the proxy secret may have. <c>trax deploy</c> mints 43.</summary>
    public const int MinimumProxySecretLength = 32;
}
