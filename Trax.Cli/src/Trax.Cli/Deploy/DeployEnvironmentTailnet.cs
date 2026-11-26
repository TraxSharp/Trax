namespace Trax.Cli.Deploy;

/// <summary>
/// A <c>tailnet</c> exposure's names: the hub's machine name on the tailnet, and on a cluster the namespace the
/// Tailscale operator runs its proxies in.
/// </summary>
internal sealed class DeployEnvironmentTailnet
{
    /// <summary>The operator's namespace when the environment names none.</summary>
    public const string DefaultOperatorNamespace = "tailscale";

    /// <summary>
    /// The hub's machine name on the tailnet, which MagicDNS serves as <c>&lt;hostname&gt;.&lt;tailnet&gt;.ts.net</c>.
    /// Without it, <c>&lt;app&gt;-&lt;environment&gt;</c>.
    /// </summary>
    public string? Hostname { get; set; }

    /// <summary>
    /// On a cluster, the namespace the Tailscale operator runs its proxy pods in, which the hub's network policy
    /// admits. Without it, <see cref="DefaultOperatorNamespace"/>. Not read under podman.
    /// </summary>
    public string? OperatorNamespace { get; set; }
}
