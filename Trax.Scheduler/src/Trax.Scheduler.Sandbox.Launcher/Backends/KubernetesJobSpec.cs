using System.Globalization;
using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// The Jobs one sandboxed run becomes on a cluster: the sandbox, and after it exits the collector
/// that reads its output. Everything in them comes from the launcher's configuration and the
/// checked limits; from the request, only the run id, as a name and a path. Both meet Pod
/// Security <c>restricted</c> and the namespace's admission policy
/// (<see cref="KubernetesSandboxManifests"/>), which refuses anything else.
/// </summary>
internal static class KubernetesJobSpec
{
    public const string ManagedByLabel = "app.kubernetes.io/managed-by";
    public const string ManagedBy = "trax-sandbox-launcher";
    public const string RunLabel = "trax.sandbox/run";
    public const string RunnerServiceAccount = "trax-sandbox-runner";
    public const string RunsVolume = "runs";
    public const string ScratchVolume = "scratch";
    public const string OutputVolume = "out";
    public const string RoleLabel = "trax.sandbox/role";

    /// <summary>Where the run's directory keeps what the collector wrote.</summary>
    public const string Collected = "collected";

    /// <summary>Where the collector mounts the run's output volume, and the run's collected directory.</summary>
    public const string CollectFrom = "/collect/out";
    public const string CollectTo = "/collect/run";

    public static string JobName(string runId) => $"trax-sbx-{runId}";

    public static string CollectorName(string runId) => $"trax-sbx-{runId}-collect";

    /// <summary>The claim Kubernetes makes for a pod's generic ephemeral output volume: the pod's name and the volume's.</summary>
    public static string OutputClaimName(string podName) => $"{podName}-{OutputVolume}";

    public static V1Job Build(SandboxJob job, KubernetesBackendOptions kubernetes)
    {
        var (uid, gid) = Ids(job);
        var pod = Hardened(kubernetes, uid, gid);
        pod.Containers = [Container(job, uid, gid)];
        pod.Volumes =
        [
            new V1Volume
            {
                Name = RunsVolume,
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = kubernetes.RunsClaimName,
                    ReadOnlyProperty = true,
                },
            },
            new V1Volume
            {
                Name = ScratchVolume,
                EmptyDir = new V1EmptyDirVolumeSource
                {
                    Medium = "Memory",
                    SizeLimit = Mi(job.Limits.DiskMiB),
                },
            },
            OutputVolumeOf(job, kubernetes),
        ];
        return Wrap(JobName(job.RunId), "sandbox", job, kubernetes, job.Limits.TimeoutSeconds, pod);
    }

    /// <summary>
    /// The run's own output volume: a claim of exactly its declared disk on the configured class,
    /// made with the pod and deleted with it, so the size is the storage's to enforce rather than
    /// a walk's to notice. No data source: a new, empty volume and nothing cloned.
    /// </summary>
    private static V1Volume OutputVolumeOf(SandboxJob job, KubernetesBackendOptions kubernetes) =>
        new()
        {
            Name = OutputVolume,
            Ephemeral = new V1EphemeralVolumeSource
            {
                VolumeClaimTemplate = new V1PersistentVolumeClaimTemplate
                {
                    Spec = new V1PersistentVolumeClaimSpec
                    {
                        AccessModes = ["ReadWriteOnce"],
                        StorageClassName = kubernetes.OutputStorageClassName,
                        VolumeMode = "Filesystem",
                        Resources = new V1VolumeResourceRequirements
                        {
                            Requests = new Dictionary<string, ResourceQuantity>(
                                StringComparer.Ordinal
                            )
                            {
                                ["storage"] = Mi(job.Limits.DiskMiB),
                            },
                        },
                    },
                },
            },
        };

    /// <summary>
    /// The collector for a run whose sandbox has exited: the launcher's image, run as the
    /// profile's user so it can read what the sandbox wrote, with the sandbox's output volume
    /// read-only and only that run's collected directory writable. It runs nothing of the
    /// sandbox's: it reads the files as data, checks them as the launcher would, and writes a tar.
    /// </summary>
    public static V1Job BuildCollector(
        SandboxJob job,
        string outputClaim,
        KubernetesBackendOptions kubernetes,
        SandboxCaps caps
    )
    {
        var (uid, gid) = Ids(job);
        var pod = Hardened(kubernetes, uid, gid);
        pod.Containers = [CollectorContainer(job, kubernetes, caps, uid, gid)];
        pod.Volumes =
        [
            new V1Volume
            {
                Name = OutputVolume,
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = outputClaim,
                    ReadOnlyProperty = true,
                },
            },
            new V1Volume
            {
                Name = RunsVolume,
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = kubernetes.RunsClaimName,
                },
            },
            new V1Volume
            {
                Name = ScratchVolume,
                EmptyDir = new V1EmptyDirVolumeSource { Medium = "Memory", SizeLimit = Mi(64) },
            },
        ];
        return Wrap(CollectorName(job.RunId), "collector", job, kubernetes, 600, pod);
    }

    private static V1Container CollectorContainer(
        SandboxJob job,
        KubernetesBackendOptions kubernetes,
        SandboxCaps caps,
        long uid,
        long gid
    )
    {
        var limits = PodmanSandboxBackend.OutputLimits(caps);
        var resources = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
        {
            ["cpu"] = new("500m"),
            // The output is read into a tar in memory and written once more; room for both.
            ["memory"] = Mi((int)Math.Min(int.MaxValue, 256 + 3 * (limits.MaxBytes / MiB))),
            ["ephemeral-storage"] = Mi(16),
        };
        return new V1Container
        {
            Name = "collector",
            Image = kubernetes.CollectorImage,
            ImagePullPolicy = "IfNotPresent",
            Args =
            [
                $"--{TraxSandboxLauncher.CollectArgument}={CollectFrom}",
                $"--{TraxSandboxLauncher.CollectToArgument}={CollectTo}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"--{TraxSandboxLauncher.CollectMaxBytesArgument}={limits.MaxBytes}"
                ),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"--{TraxSandboxLauncher.CollectMaxFilesArgument}={limits.MaxFiles}"
                ),
            ],
            Env =
            [
                new V1EnvVar { Name = "HOME", Value = SandboxPaths.Scratch },
                new V1EnvVar { Name = "TMPDIR", Value = SandboxPaths.Scratch },
            ],
            Resources = new V1ResourceRequirements
            {
                Limits = resources,
                Requests = new Dictionary<string, ResourceQuantity>(
                    resources,
                    StringComparer.Ordinal
                ),
            },
            SecurityContext = Restricted(uid, gid),
            VolumeMounts =
            [
                new V1VolumeMount
                {
                    Name = OutputVolume,
                    MountPath = CollectFrom,
                    ReadOnlyProperty = true,
                },
                new V1VolumeMount
                {
                    Name = RunsVolume,
                    SubPath = $"{job.RunId}/{Collected}",
                    MountPath = CollectTo,
                },
                new V1VolumeMount { Name = ScratchVolume, MountPath = SandboxPaths.Scratch },
            ],
        };
    }

    private const long MiB = 1024 * 1024;

    private static (long Uid, long Gid) Ids(SandboxJob job)
    {
        var user = SandboxProfile.UserIds.Match(job.Profile.User);
        return (
            long.Parse(user.Groups["uid"].Value, CultureInfo.InvariantCulture),
            long.Parse(user.Groups["gid"].Value, CultureInfo.InvariantCulture)
        );
    }

    private static V1Job Wrap(
        string name,
        string role,
        SandboxJob job,
        KubernetesBackendOptions kubernetes,
        int deadlineSeconds,
        V1PodSpec pod
    )
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedByLabel] = ManagedBy,
            [RunLabel] = job.RunId,
            [RoleLabel] = role,
        };
        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = kubernetes.Namespace,
                Labels = labels,
            },
            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                ActiveDeadlineSeconds = deadlineSeconds,
                TtlSecondsAfterFinished = 600,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal),
                    },
                    Spec = pod,
                },
            },
        };
    }

    /// <summary>What every pod the launcher makes is: pinned, tokenless, off the host and the network.</summary>
    private static V1PodSpec Hardened(KubernetesBackendOptions kubernetes, long uid, long gid) =>
        new()
        {
            RestartPolicy = "Never",
            RuntimeClassName = kubernetes.RuntimeClassName,
            ServiceAccountName = RunnerServiceAccount,
            AutomountServiceAccountToken = false,
            EnableServiceLinks = false,
            HostNetwork = false,
            HostPID = false,
            HostIPC = false,
            ShareProcessNamespace = false,
            // No resolver: a sandbox has nothing to look up, and DNS is a way out where a
            // network policy is not enforced.
            DnsPolicy = "None",
            DnsConfig = new V1PodDNSConfig { Nameservers = ["127.0.0.1"] },
            TerminationGracePeriodSeconds = 0,
            Affinity = SameNode(kubernetes.NodeName),
            SecurityContext = new V1PodSecurityContext
            {
                RunAsNonRoot = true,
                RunAsUser = uid,
                RunAsGroup = gid,
                // The output volume is made owned by root; the group lets the profile's user write it.
                FsGroup = gid,
                FsGroupChangePolicy = "OnRootMismatch",
                SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
            },
        };

    private static V1SecurityContext Restricted(long uid, long gid) =>
        new()
        {
            AllowPrivilegeEscalation = false,
            Privileged = false,
            ReadOnlyRootFilesystem = true,
            RunAsNonRoot = true,
            RunAsUser = uid,
            RunAsGroup = gid,
            Capabilities = new V1Capabilities { Drop = ["ALL"] },
            SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
        };

    private static V1Container Container(SandboxJob job, long uid, long gid)
    {
        var resources = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
        {
            ["cpu"] = new(
                string.Create(CultureInfo.InvariantCulture, $"{job.Limits.CpuMillicores}m")
            ),
            ["memory"] = Mi(job.Limits.MemoryMiB),
            ["ephemeral-storage"] = Mi(job.Limits.DiskMiB),
        };
        return new V1Container
        {
            Name = "sandbox",
            Image = job.Profile.Image,
            ImagePullPolicy = "IfNotPresent",
            Command = job.Profile.Command.Count > 0 ? [.. job.Profile.Command] : null,
            Env =
            [
                new V1EnvVar { Name = "HOME", Value = SandboxPaths.Scratch },
                new V1EnvVar { Name = "TMPDIR", Value = SandboxPaths.Scratch },
            ],
            Resources = new V1ResourceRequirements
            {
                Limits = resources,
                Requests = new Dictionary<string, ResourceQuantity>(
                    resources,
                    StringComparer.Ordinal
                ),
            },
            SecurityContext = Restricted(uid, gid),
            VolumeMounts =
            [
                new V1VolumeMount
                {
                    Name = RunsVolume,
                    SubPath = $"{job.RunId}/{SandboxPaths.Input}",
                    MountPath = $"{SandboxPaths.Root}/{SandboxPaths.Input}",
                    ReadOnlyProperty = true,
                },
                new V1VolumeMount
                {
                    Name = OutputVolume,
                    MountPath = $"{SandboxPaths.Root}/{SandboxPaths.Output}",
                },
                new V1VolumeMount { Name = ScratchVolume, MountPath = SandboxPaths.Scratch },
            ],
        };
    }

    private static V1Affinity SameNode(string node) =>
        new()
        {
            NodeAffinity = new V1NodeAffinity
            {
                RequiredDuringSchedulingIgnoredDuringExecution = new V1NodeSelector
                {
                    NodeSelectorTerms =
                    [
                        new V1NodeSelectorTerm
                        {
                            MatchFields =
                            [
                                new V1NodeSelectorRequirement
                                {
                                    Key = "metadata.name",
                                    OperatorProperty = "In",
                                    Values = [node],
                                },
                            ],
                        },
                    ],
                },
            },
        };

    private static ResourceQuantity Mi(int mib) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{mib}Mi"));
}
