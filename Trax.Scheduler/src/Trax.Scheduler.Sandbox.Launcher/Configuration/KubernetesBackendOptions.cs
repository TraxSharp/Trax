namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>Where the launcher creates sandbox Jobs on a cluster.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher#a-cluster">SDK reference</seealso>
public sealed class KubernetesBackendOptions
{
    /// <summary>The dedicated namespace sandboxes run in. Nothing else runs there.</summary>
    public string Namespace { get; set; } = "trax-sandbox";

    /// <summary>The RuntimeClass every sandbox pod names. The startup canary proves what it maps to.</summary>
    public string RuntimeClassName { get; set; } = "kata";

    /// <summary>
    /// The StorageClass of each run's own output volume, a generic ephemeral volume of the run's
    /// declared disk: the run's disk ceiling, so the class must enforce capacity (TopoLVM,
    /// Longhorn, a cloud block-storage driver). Required. The launcher refuses to start when the
    /// class does not exist or is a host-path provisioner, such as k3s's <c>local-path</c>, which
    /// enforce no size; it cannot check the rest, so naming a class here is the deploy's word that
    /// it does.
    /// </summary>
    public string OutputStorageClassName { get; set; } = "";

    /// <summary>
    /// The image that collects a run's output after the sandbox exits: the launcher's own, pinned
    /// by digest. It runs as a Job of its own that mounts the run's output volume read-only, checks
    /// it entry by entry, and writes what it accepted as a tar to the run's directory on the runs
    /// volume. <see cref="KubernetesSandboxManifests"/> sets it to the launcher image.
    /// </summary>
    public string CollectorImage { get; set; } = "";

    /// <summary>The PersistentVolumeClaim, in <see cref="Namespace"/>, that holds each run's input and collected output.</summary>
    public string RunsClaimName { get; set; } = "trax-sandbox-runs";

    /// <summary>Where the launcher's own pod mounts <see cref="RunsClaimName"/>.</summary>
    public string RunsPath { get; set; } = "/runs";

    /// <summary>
    /// The node the launcher runs on; sandboxes are pinned to it, so a ReadWriteOnce claim works
    /// and the canary compares the right kernel. Read from the <c>NODE_NAME</c> environment
    /// variable (the downward API) when not set.
    /// </summary>
    public string NodeName { get; set; } = "";

    /// <summary>How often a running sandbox's status and the runs volume's free space are checked. Two seconds by default.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The share of the runs volume's filesystem, in percent, that must stay free. Below it no run
    /// starts, and every running one is killed at its next poll. The runs volume holds each run's
    /// input and collected output; a run's own output volume has a size of its own. 15 by default,
    /// above the kubelet's default eviction threshold of 10.
    /// </summary>
    public int RunsMinFreePercent { get; set; } = 15;
}
