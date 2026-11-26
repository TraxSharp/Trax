namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// How a deployment says what reaches a host, and the secret its proxy proves itself with. A host cannot see from the
/// inside whether it is on a public address or reachable only through a tailnet proxy, so <c>trax deploy</c> tells it:
/// it sets <see cref="Variable"/> to <see cref="Tailnet"/> only on a hub it exposes through a Tailscale proxy it
/// rendered itself, and gives that hub and that proxy <see cref="ProxySecretVariable"/>, a value minted on every deploy.
/// The proxy sends it in <see cref="ProxySecretHeader"/>. The tailnet sign-in scheme (<c>AddTraxTailscaleAuth</c>)
/// refuses to start unless both are set, and reads no identity from a request that does not carry the secret.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/deploy-exposure">SDK reference</seealso>
public static class DeployExposure
{
    /// <summary>The configuration key a host reads its exposure from.</summary>
    public const string ConfigurationKey = "Trax:Deploy:Exposure";

    /// <summary>The environment variable <c>trax deploy</c> sets, which configuration reads as <see cref="ConfigurationKey"/>.</summary>
    public const string Variable = "Trax__Deploy__Exposure";

    /// <summary>
    /// The exposure of a hub reached only through a Tailscale proxy the deployment controls: no public address, no
    /// published host port.
    /// </summary>
    public const string Tailnet = "tailnet";

    /// <summary>The configuration key a host reads the proxy's secret from.</summary>
    public const string ProxySecretKey = "Trax:Deploy:ProxySecret";

    /// <summary>The environment variable that carries the proxy's secret, read as <see cref="ProxySecretKey"/>.</summary>
    public const string ProxySecretVariable = "Trax__Deploy__ProxySecret";

    /// <summary>The request header the proxy puts the secret in, replacing any copy the client sent.</summary>
    public const string ProxySecretHeader = "Trax-Proxy-Secret";

    /// <summary>
    /// The name of the tailnet sign-in scheme, as a host reports it in <see cref="DeployManifest.Authentication"/>.
    /// <c>trax deploy</c> refuses a target that is not exposed on a tailnet when a host lists it.
    /// </summary>
    public const string TailscaleScheme = "TraxTailscale";
}
