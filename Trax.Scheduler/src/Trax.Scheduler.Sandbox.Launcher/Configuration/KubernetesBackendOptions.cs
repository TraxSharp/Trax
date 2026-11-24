namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>Where the launcher creates sandbox Jobs on a cluster.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher#a-cluster">SDK reference</seealso>
public sealed class KubernetesBackendOptions
{
    /// <summary>The dedicated namespace sandboxes run in. Nothing else runs there.</summary>
    public string Namespace { get; set; } = "trax-sandbox";

    /// <summary>The RuntimeClass every sandbox pod names. The startup canary proves what it maps to.</summary>
    public string RuntimeClassName { get; set; } = "kata";

    /// <summary>The PersistentVolumeClaim, in <see cref="Namespace"/>, that holds each run's input and output directories.</summary>
    public string RunsClaimName { get; set; } = "trax-sandbox-runs";

    /// <summary>Where the launcher's own pod mounts <see cref="RunsClaimName"/>.</summary>
    public string RunsPath { get; set; } = "/runs";

    /// <summary>
    /// The node the launcher runs on; sandboxes are pinned to it, so a ReadWriteOnce claim works
    /// and the canary compares the right kernel. Read from the <c>NODE_NAME</c> environment
    /// variable (the downward API) when not set.
    /// </summary>
    public string NodeName { get; set; } = "";

    /// <summary>How often a running sandbox's status and output size are checked. Two seconds by default.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
