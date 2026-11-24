namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>The isolation every sandbox runs under. The launcher refuses to start sandboxes when its startup canary finds otherwise.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public enum SandboxRuntime
{
    /// <summary>Kata Containers: each sandbox is a microVM with its own guest kernel. The default, and the decided runtime.</summary>
    Kata,

    /// <summary>
    /// gVisor: a user-space kernel, not a VM. Weaker against a kernel escape than Kata; chosen
    /// only by naming it, for a cluster that offers nothing else (GKE Sandbox).
    /// </summary>
    GVisor,
}
