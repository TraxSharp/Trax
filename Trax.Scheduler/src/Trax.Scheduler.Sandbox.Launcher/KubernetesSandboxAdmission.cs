using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;

namespace Trax.Scheduler.Sandbox.Launcher;

/// <summary>
/// The ValidatingAdmissionPolicies of the sandbox namespace. The launcher's Role lets it create
/// any Job there, and Pod Security <c>restricted</c> still allows a runc pod, any image, any
/// ServiceAccount and the environment and volumes a credential webhook injects. These close that:
/// a Job, and a pod that is not the launcher's own, must name the configured RuntimeClass, run
/// one container from the deploy's digest allowlist, use the tokenless sandbox ServiceAccount,
/// carry only HOME and TMPDIR, mount only the runs claim and an emptyDir, and stay off the host's
/// namespaces. The launcher's own pod must run the launcher image under its own ServiceAccount.
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
            JobVariables(),
            SandboxRules(options, "spec").Append(NoLauncherLabelOnJobs()).ToList()
        );
        yield return Binding(JobPolicyName, ns);
        yield return Policy(
            PodPolicyName,
            "",
            "pods",
            PodVariables(),
            PodPolicyRules(options, launcherImage).ToList()
        );
        yield return Binding(PodPolicyName, ns);
    }

    private static List<V1Variable> JobVariables() =>
        [new V1Variable { Name = "spec", Expression = "object.spec.template.spec" }];

    private static List<V1Variable> PodVariables() =>
        [
            new V1Variable { Name = "spec", Expression = "object.spec" },
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
        yield return (
            $"!has({s}.volumes) || {s}.volumes.all(v, (has(v.persistentVolumeClaim) && v.persistentVolumeClaim.claimName == '{options.Kubernetes.RunsClaimName}') || has(v.emptyDir))",
            "a sandbox mounts only the runs claim and an emptyDir"
        );
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
            $"size({s}.containers) == 1 && {s}.containers.all(c, c.image in [{images}])",
            "a sandbox runs one container from the deploy's image allowlist"
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
