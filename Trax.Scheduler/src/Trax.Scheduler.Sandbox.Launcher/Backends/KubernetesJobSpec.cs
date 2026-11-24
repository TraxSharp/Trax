using System.Globalization;
using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// The Job one sandboxed run becomes on a cluster. Everything in it comes from the launcher's
/// configuration and the checked limits; from the request, only the run id, as a name and a path.
/// It meets Pod Security <c>restricted</c> and the namespace's admission policy
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

    public static string JobName(string runId) => $"trax-sbx-{runId}";

    public static V1Job Build(SandboxJob job, KubernetesBackendOptions kubernetes)
    {
        var user = SandboxProfile.UserIds.Match(job.Profile.User);
        var uid = long.Parse(user.Groups["uid"].Value, CultureInfo.InvariantCulture);
        var gid = long.Parse(user.Groups["gid"].Value, CultureInfo.InvariantCulture);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedByLabel] = ManagedBy,
            [RunLabel] = job.RunId,
        };

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta
            {
                Name = JobName(job.RunId),
                NamespaceProperty = kubernetes.Namespace,
                Labels = labels,
            },
            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                ActiveDeadlineSeconds = job.Limits.TimeoutSeconds,
                TtlSecondsAfterFinished = 600,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal),
                    },
                    Spec = Pod(job, kubernetes, uid, gid),
                },
            },
        };
    }

    private static V1PodSpec Pod(
        SandboxJob job,
        KubernetesBackendOptions kubernetes,
        long uid,
        long gid
    ) =>
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
                SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
            },
            Containers = [Container(job, uid, gid)],
            Volumes =
            [
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
                    EmptyDir = new V1EmptyDirVolumeSource
                    {
                        Medium = "Memory",
                        SizeLimit = Mi(job.Limits.DiskMiB),
                    },
                },
            ],
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
            SecurityContext = new V1SecurityContext
            {
                AllowPrivilegeEscalation = false,
                Privileged = false,
                ReadOnlyRootFilesystem = true,
                RunAsNonRoot = true,
                RunAsUser = uid,
                RunAsGroup = gid,
                Capabilities = new V1Capabilities { Drop = ["ALL"] },
                SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
            },
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
                    Name = RunsVolume,
                    SubPath = $"{job.RunId}/{SandboxPaths.Output}",
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
