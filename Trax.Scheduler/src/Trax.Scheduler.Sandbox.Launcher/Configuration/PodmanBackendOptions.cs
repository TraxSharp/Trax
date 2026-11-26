namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>How the launcher reaches podman on one host.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher#one-host">SDK reference</seealso>
public sealed class PodmanBackendOptions
{
    /// <summary>
    /// The podman API socket, such as <c>/run/user/1001/podman/podman.sock</c>. Only the
    /// launcher holds it: never mount it into a sandbox or the hub.
    /// </summary>
    public string SocketPath { get; set; } = "";

    /// <summary>
    /// The image of the holder: a launcher-owned container that keeps each run's tmpfs volumes
    /// mounted while the sandbox runs and after it exits, so the launcher can read the output.
    /// Pinned by digest; a pause image (it only blocks) is the right kind.
    /// </summary>
    public string HolderImage { get; set; } = "";

    /// <summary>The holder's command, when its image's own does not block forever.</summary>
    public IList<string> HolderCommand { get; } = [];
}
