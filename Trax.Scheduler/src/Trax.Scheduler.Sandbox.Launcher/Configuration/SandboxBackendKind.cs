namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>Where the launcher starts sandboxes.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public enum SandboxBackendKind
{
    /// <summary>
    /// Rootless podman on one host, through its API socket, which only the launcher holds. It
    /// cannot run Kata (Kata 4 has no OCI command line), so it needs <see cref="SandboxRuntime.GVisor"/>;
    /// for Kata on one machine, run k3s and use <see cref="Kubernetes"/>.
    /// </summary>
    Podman,

    /// <summary>A Kubernetes cluster, k3s on one machine included: one Job per run in a dedicated namespace.</summary>
    Kubernetes,
}
