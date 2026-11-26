using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;

namespace Trax.Scheduler.Sandbox.Launcher;

/// <summary>
/// The ValidatingAdmissionPolicies of the sandbox namespace. The launcher's Role lets it create
/// any Job there, and Pod Security <c>restricted</c> still allows a runc pod, any image, any
/// ServiceAccount and the environment and volumes a credential webhook injects. These close that:
/// a Job, and a pod that is not the launcher's own, must name the configured RuntimeClass, run
/// one container, use the tokenless sandbox ServiceAccount, carry only HOME and TMPDIR, and stay
/// off the host's namespaces. A sandbox runs an image from the deploy's digest allowlist and
/// mounts only the runs claim read-only, an emptyDir, and one generic ephemeral volume of exactly
/// the output shape (the configured class, ReadWriteOnce, no data source, no larger than the disk
/// cap). A collector runs the launcher image and mounts only the runs claim, an emptyDir, and a
/// run's output claim read-only. The launcher's own pod must run the launcher image under its own
/// ServiceAccount.
/// </summary>
internal static class KubernetesSandboxAdmission
{
    public const string JobPolicyName = "trax-sandbox-jobs";
    public const string PodPolicyName = "trax-sandbox-pods";

    public static IEnumerable<object> Objects(SandboxLauncherOptions options, string launcherImage)
    {
        var ns = options.Kubernetes.Namespace;
        yield return Policy(
            JobPolicyName,
            "batch",
            "jobs",
            JobVariables(launcherImage),
            SandboxRules(options, "spec").Append(NoLauncherLabelOnJobs()).ToList()
        );
        yield return Binding(JobPolicyName, ns);
        yield return Policy(
            PodPolicyName,
            "",
            "pods",
            PodVariables(launcherImage),
            PodPolicyRules(options, launcherImage).ToList()
        );
        yield return Binding(PodPolicyName, ns);
    }

    private static List<V1Variable> JobVariables(string launcherImage) =>
        [
            new V1Variable { Name = "spec", Expression = "object.spec.template.spec" },
            Collector(launcherImage),
        ];

    /// <summary>Whether the pod spec is a collector's: every container the launcher image.</summary>
    private static V1Variable Collector(string launcherImage) =>
        new()
        {
            Name = "collector",
            Expression = $"variables.spec.containers.all(c, c.image == '{launcherImage}')",
        };

    private static List<V1Variable> PodVariables(string launcherImage) =>
        [
            new V1Variable { Name = "spec", Expression = "object.spec" },
            Collector(launcherImage),
            new V1Variable
            {
                Name = "launcher",
                Expression =
                    "has(object.metadata.labels) && 'app.kubernetes.io/name' in object.metadata.labels"
                    + $" && object.metadata.labels['app.kubernetes.io/name'] == '{KubernetesSandboxManifests.LauncherName}'",
            },
        ];

    /// <summary>What every sandbox pod spec must be; <paramref name="spec"/> names the CEL variable holding it.</summary>
    internal static IEnumerable<V1Validation> SandboxRules(
        SandboxLauncherOptions options,
        string spec,
        string guard = ""
    )
    {
        var s = $"variables.{spec}";
        return PodRules(options, s)
            .Concat(ContainerRules(options, s))
            .Select(r =>
                Check(guard.Length == 0 ? r.Expression : $"{guard} || ({r.Expression})", r.Message)
            );
    }

    private static IEnumerable<(string Expression, string Message)> PodRules(
        SandboxLauncherOptions options,
        string s
    )
    {
        yield return (
            $"has({s}.runtimeClassName) && {s}.runtimeClassName == '{options.Kubernetes.RuntimeClassName}'",
            "a sandbox must name the RuntimeClass the launcher is configured with"
        );
        yield return (
            $"has({s}.automountServiceAccountToken) && {s}.automountServiceAccountToken == false",
            "a sandbox must not mount a ServiceAccount token"
        );
        yield return (
            $"has({s}.serviceAccountName) && {s}.serviceAccountName == '{KubernetesJobSpec.RunnerServiceAccount}'",
            "a sandbox must run as the tokenless sandbox ServiceAccount"
        );
        yield return (
            $"(!has({s}.hostNetwork) || !{s}.hostNetwork) && (!has({s}.hostPID) || !{s}.hostPID) && (!has({s}.hostIPC) || !{s}.hostIPC)",
            "a sandbox must not share the host's namespaces"
        );
        yield return (
            $"(!has({s}.initContainers) || size({s}.initContainers) == 0) && (!has({s}.ephemeralContainers) || size({s}.ephemeralContainers) == 0)",
            "a sandbox has no init or ephemeral containers"
        );
        var runs = options.Kubernetes.RunsClaimName;
        const string ReadOnlyClaim =
            "has(v.persistentVolumeClaim.readOnly) && v.persistentVolumeClaim.readOnly";
        var sandbox =
            $"(has(v.persistentVolumeClaim) && v.persistentVolumeClaim.claimName == '{runs}' && {ReadOnlyClaim})"
            + $" || (has(v.ephemeral) && {OutputVolumeShape(options)})";
        var collector =
            $"has(v.persistentVolumeClaim) && (v.persistentVolumeClaim.claimName == '{runs}'"
            + $" || (v.persistentVolumeClaim.claimName.matches('^trax-sbx-[0-9a-f]{{32}}-[a-z0-9]{{5}}-{KubernetesJobSpec.OutputVolume}$') && {ReadOnlyClaim}))";
        yield return (
            $"!has({s}.volumes) || {s}.volumes.all(v, has(v.emptyDir) || (variables.collector ? ({collector}) : ({sandbox})))",
            "a sandbox mounts only the runs claim read-only, an emptyDir and its output volume; a collector only the runs claim, an emptyDir and a run's output claim read-only"
        );
    }

    /// <summary>
    /// A run's output volume and nothing broader: the configured class, ReadWriteOnce, a
    /// filesystem, no data source to clone another volume from, no selector or volume name to bind
    /// an existing one, and a request no larger than the disk cap.
    /// </summary>
    private static string OutputVolumeShape(SandboxLauncherOptions options)
    {
        const string T = "v.ephemeral.volumeClaimTemplate.spec";
        return $"has(v.ephemeral.volumeClaimTemplate) && has({T}.storageClassName) && {T}.storageClassName == '{options.Kubernetes.OutputStorageClassName}'"
            + $" && has({T}.accessModes) && {T}.accessModes == ['ReadWriteOnce']"
            + $" && !has({T}.dataSource) && !has({T}.dataSourceRef) && !has({T}.selector) && !has({T}.volumeName)"
            + $" && (!has({T}.volumeMode) || {T}.volumeMode == 'Filesystem')"
            + $" && has({T}.resources) && has({T}.resources.requests) && 'storage' in {T}.resources.requests"
            + $" && quantity(string({T}.resources.requests['storage'])).compareTo(quantity('{options.Caps.MaxDiskMiB.ToString(System.Globalization.CultureInfo.InvariantCulture)}Mi')) <= 0";
    }

    private static IEnumerable<(string Expression, string Message)> ContainerRules(
        SandboxLauncherOptions options,
        string s
    )
    {
        var images = string.Join(
            ", ",
            options
                .Profiles.Values.Select(p => $"'{p.Image}'")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        );
        yield return (
            $"size({s}.containers) == 1 && (variables.collector || {s}.containers.all(c, c.image in [{images}]))",
            "a sandbox runs one container from the deploy's image allowlist, a collector the launcher image"
        );
        yield return (
            $"{s}.containers.all(c, !has(c.envFrom) && (!has(c.env) || c.env.all(e, (e.name == 'HOME' || e.name == 'TMPDIR') && !has(e.valueFrom))))",
            "a sandbox carries no environment but HOME and TMPDIR"
        );
        yield return (
            $"{s}.containers.all(c, has(c.securityContext) && (!has(c.securityContext.privileged) || !c.securityContext.privileged) && has(c.securityContext.allowPrivilegeEscalation) && !c.securityContext.allowPrivilegeEscalation && (!has(c.securityContext.capabilities) || !has(c.securityContext.capabilities.add) || size(c.securityContext.capabilities.add) == 0))",
            "a sandbox is unprivileged, cannot escalate, and adds no capability"
        );
    }

    private static V1Validation NoLauncherLabelOnJobs() =>
        Check(
            "!has(object.spec.template.metadata) || !has(object.spec.template.metadata.labels) || !('app.kubernetes.io/name' in object.spec.template.metadata.labels)",
            "a Job's pods may not be labelled as the launcher"
        );

    private static IEnumerable<V1Validation> PodPolicyRules(
        SandboxLauncherOptions options,
        string launcherImage
    )
    {
        foreach (var rule in SandboxRules(options, "spec", guard: "variables.launcher"))
            yield return rule;
        yield return Check(
            $"!variables.launcher || (object.spec.serviceAccountName == '{KubernetesSandboxManifests.LauncherName}' && object.spec.containers.all(c, c.image == '{launcherImage}') && (!has(object.spec.initContainers) || size(object.spec.initContainers) == 0))",
            "the launcher's pod runs only the launcher image, as the launcher's ServiceAccount"
        );
    }

    private static V1Validation Check(string expression, string message) =>
        new()
        {
            Expression = expression,
            Message = message,
            Reason = "Forbidden",
        };

    private static V1ValidatingAdmissionPolicy Policy(
        string name,
        string group,
        string resource,
        List<V1Variable> variables,
        List<V1Validation> validations
    ) =>
        new()
        {
            ApiVersion = "admissionregistration.k8s.io/v1",
            Kind = "ValidatingAdmissionPolicy",
            Metadata = new V1ObjectMeta { Name = name },
            Spec = new V1ValidatingAdmissionPolicySpec
            {
                FailurePolicy = "Fail",
                MatchConstraints = new V1MatchResources
                {
                    ResourceRules =
                    [
                        new V1NamedRuleWithOperations
                        {
                            ApiGroups = [group],
                            ApiVersions = ["v1"],
                            Operations = ["CREATE", "UPDATE"],
                            Resources = [resource],
                        },
                    ],
                },
                Variables = variables,
                Validations = validations,
            },
        };

    private static V1ValidatingAdmissionPolicyBinding Binding(string policy, string ns) =>
        new()
        {
            ApiVersion = "admissionregistration.k8s.io/v1",
            Kind = "ValidatingAdmissionPolicyBinding",
            Metadata = new V1ObjectMeta { Name = policy },
            Spec = new V1ValidatingAdmissionPolicyBindingSpec
            {
                PolicyName = policy,
                ValidationActions = ["Deny"],
                MatchResources = new V1MatchResources
                {
                    NamespaceSelector = new V1LabelSelector
                    {
                        MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["kubernetes.io/metadata.name"] = ns,
                        },
                    },
                },
            },
        };
}
