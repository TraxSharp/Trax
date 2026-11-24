namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>Where the launcher starts sandboxes.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public enum SandboxBackendKind
{
    /// <summary>Rootless podman on one host, through its API socket, which only the launcher holds.</summary>
    Podman,

    /// <summary>A Kubernetes cluster: one Job per run in a dedicated namespace.</summary>
    Kubernetes,
}
