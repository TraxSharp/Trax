using System.Globalization;
using System.Text;
using k8s;
using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;

namespace Trax.Scheduler.Sandbox.Launcher;

/// <summary>
/// The objects a cluster needs before the launcher can run there, rendered from the launcher's
/// own options so the two cannot disagree: the dedicated namespace at Pod Security
/// <c>restricted</c>, the launcher's ServiceAccount and Role (Jobs, and pods read-only; no
/// <c>pods/exec</c> or <c>pods/log</c>), a ServiceAccount for sandboxes with no token and no
/// binding, a deny-all NetworkPolicy for sandboxes, the runs volume, the launcher itself, and a
/// ValidatingAdmissionPolicy that refuses any Job or pod in the namespace that does not look like
/// one the launcher makes.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher#a-cluster">SDK reference</seealso>
public static class KubernetesSandboxManifests
{
    /// <summary>The launcher's name: its Deployment, Service, ServiceAccount and Role.</summary>
    public const string LauncherName = "trax-sandbox-launcher";

    /// <summary>The Secret holding the launcher's key, base64, under <c>signing-key</c>.</summary>
    public const string KeySecretName = "trax-sandbox-launcher-key";

    private const int Port = 8080;

    /// <summary>Renders every object, as one multi-document YAML.</summary>
    /// <param name="options">The launcher's options, with <see cref="SandboxBackendKind.Kubernetes"/>.</param>
    /// <param name="launcherImage">The launcher's image, pinned by digest.</param>
    /// <param name="hubNamespace">The namespace of the hub, the one place the launcher accepts connections from.</param>
    /// <param name="runsStorage">The runs volume's size, such as <c>20Gi</c>: the hard ceiling on every running sandbox's output together.</param>
    public static string Render(
        SandboxLauncherOptions options,
        string launcherImage,
        string hubNamespace,
        string runsStorage = "20Gi"
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!SandboxProfile.IsPinned(launcherImage))
            throw new ArgumentException(
                "The launcher image must be pinned as name@sha256:<digest>.",
                nameof(launcherImage)
            );
        if (options.Backend != SandboxBackendKind.Kubernetes)
            throw new ArgumentException(
                "The options are not for the Kubernetes backend.",
                nameof(options)
            );

        var yaml = new StringBuilder();
        foreach (var o in Objects(options, launcherImage, hubNamespace, runsStorage))
            yaml.Append("---\n").Append(KubernetesYaml.Serialize(o).TrimEnd('\n')).Append('\n');
        return yaml.ToString();
    }

    internal static IEnumerable<object> Objects(
        SandboxLauncherOptions options,
        string launcherImage,
        string hubNamespace,
        string runsStorage
    )
    {
        var ns = options.Kubernetes.Namespace;
        yield return Namespace(ns);
        yield return new V1ServiceAccount
        {
            ApiVersion = "v1",
            Kind = "ServiceAccount",
            Metadata = Meta(LauncherName, ns),
        };
        yield return new V1ServiceAccount
        {
            ApiVersion = "v1",
            Kind = "ServiceAccount",
            Metadata = Meta(KubernetesJobSpec.RunnerServiceAccount, ns),
            AutomountServiceAccountToken = false,
        };
        yield return Role(ns);
        yield return RoleBinding(ns);
        yield return SandboxNetworkPolicy(ns);
        yield return LauncherNetworkPolicy(ns, hubNamespace);
        yield return RunsClaim(options, runsStorage);
        yield return LauncherDeployment(options, launcherImage);
        yield return LauncherService(ns);
        foreach (var admission in KubernetesSandboxAdmission.Objects(options, launcherImage))
            yield return admission;
    }

    private static V1RoleBinding RoleBinding(string ns) =>
        new V1RoleBinding
        {
            ApiVersion = "rbac.authorization.k8s.io/v1",
            Kind = "RoleBinding",
            Metadata = Meta(LauncherName, ns),
            RoleRef = new V1RoleRef
            {
                ApiGroup = "rbac.authorization.k8s.io",
                Kind = "Role",
                Name = LauncherName,
            },
            Subjects =
            [
                new Rbacv1Subject
                {
                    Kind = "ServiceAccount",
                    Name = LauncherName,
                    NamespaceProperty = ns,
                },
            ],
        };

    private static V1Service LauncherService(string ns) =>
        new V1Service
        {
            ApiVersion = "v1",
            Kind = "Service",
            Metadata = Meta(LauncherName, ns),
            Spec = new V1ServiceSpec
            {
                Selector = Selector(),
                Ports =
                [
                    new V1ServicePort
                    {
                        Name = "http",
                        Port = Port,
                        TargetPort = Port,
                    },
                ],
            },
        };

    private static V1Namespace Namespace(string ns)
    {
        var meta = Meta(ns, ns: null);
        foreach (var mode in new[] { "enforce", "audit", "warn" })
        {
            meta.Labels[$"pod-security.kubernetes.io/{mode}"] = "restricted";
            meta.Labels[$"pod-security.kubernetes.io/{mode}-version"] = "latest";
        }
        return new V1Namespace
        {
            ApiVersion = "v1",
            Kind = "Namespace",
            Metadata = meta,
        };
    }

    /// <summary>Jobs, and pods to read their state. Nothing on pods/exec, pods/log, pods/attach, secrets or anything else.</summary>
    private static V1Role Role(string ns) =>
        new()
        {
            ApiVersion = "rbac.authorization.k8s.io/v1",
            Kind = "Role",
            Metadata = Meta(LauncherName, ns),
            Rules =
            [
                new V1PolicyRule
                {
                    ApiGroups = ["batch"],
                    Resources = ["jobs"],
                    Verbs = ["create", "get", "list", "watch", "delete"],
                },
                new V1PolicyRule
                {
                    ApiGroups = [""],
                    Resources = ["pods"],
                    Verbs = ["get", "list", "watch"],
                },
            ],
        };

    private static V1NetworkPolicy SandboxNetworkPolicy(string ns) =>
        new()
        {
            ApiVersion = "networking.k8s.io/v1",
            Kind = "NetworkPolicy",
            Metadata = Meta("trax-sandbox-deny-all", ns),
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [KubernetesJobSpec.ManagedByLabel] = KubernetesJobSpec.ManagedBy,
                    },
                },
                // Both types and no rule: nothing in, nothing out. Enforced only by a network
                // plugin that implements NetworkPolicy; the startup canary proves that it does.
                PolicyTypes = ["Ingress", "Egress"],
            },
        };

    private static V1NetworkPolicy LauncherNetworkPolicy(string ns, string hubNamespace) =>
        new()
        {
            ApiVersion = "networking.k8s.io/v1",
            Kind = "NetworkPolicy",
            Metadata = Meta($"{LauncherName}-ingress", ns),
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = Selector() },
                PolicyTypes = ["Ingress"],
                Ingress =
                [
                    new V1NetworkPolicyIngressRule
                    {
                        FromProperty =
                        [
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>(
                                        StringComparer.Ordinal
                                    )
                                    {
                                        ["kubernetes.io/metadata.name"] = hubNamespace,
                                    },
                                },
                            },
                        ],
                        Ports = [new V1NetworkPolicyPort { Protocol = "TCP", Port = Port }],
                    },
                ],
            },
        };

    private static V1PersistentVolumeClaim RunsClaim(
        SandboxLauncherOptions options,
        string storage
    ) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "PersistentVolumeClaim",
            Metadata = Meta(options.Kubernetes.RunsClaimName, options.Kubernetes.Namespace),
            Spec = new V1PersistentVolumeClaimSpec
            {
                AccessModes = ["ReadWriteOnce"],
                Resources = new V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                    {
                        ["storage"] = new ResourceQuantity(storage),
                    },
                },
            },
        };

    /// <summary>The launcher's configuration as environment variables, the key from its Secret.</summary>
    private static List<V1EnvVar> LauncherEnv(SandboxLauncherOptions options)
    {
        var kube = options.Kubernetes;
        var env = new List<V1EnvVar>
        {
            Field("NODE_NAME", "spec.nodeName"),
            Field("POD_IP", "status.podIP"),
            Value("Trax__SandboxLauncher__Backend", "Kubernetes"),
            Value("Trax__SandboxLauncher__Runtime", options.Runtime.ToString()),
            Value("Trax__SandboxLauncher__ListenUrl", $"http://0.0.0.0:{Port}"),
            Value("Trax__SandboxLauncher__Kubernetes__Namespace", kube.Namespace),
            Value("Trax__SandboxLauncher__Kubernetes__RuntimeClassName", kube.RuntimeClassName),
            Value("Trax__SandboxLauncher__Kubernetes__RunsClaimName", kube.RunsClaimName),
            Value("Trax__SandboxLauncher__Kubernetes__RunsPath", kube.RunsPath),
            new V1EnvVar
            {
                Name = "Trax__SandboxLauncher__SigningKey",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = KeySecretName,
                        Key = "signing-key",
                    },
                },
            },
        };
        foreach (
            var (name, profile) in options.Profiles.OrderBy(p => p.Key, StringComparer.Ordinal)
        )
        {
            env.Add(Value($"Trax__SandboxLauncher__Profiles__{name}__Image", profile.Image));
            env.Add(Value($"Trax__SandboxLauncher__Profiles__{name}__User", profile.User));
            for (var i = 0; i < profile.Command.Count; i++)
                env.Add(
                    Value(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Trax__SandboxLauncher__Profiles__{name}__Command__{i}"
                        ),
                        profile.Command[i]
                    )
                );
        }
        return env;
    }

    private static V1Deployment LauncherDeployment(SandboxLauncherOptions options, string image)
    {
        return new V1Deployment
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = Meta(LauncherName, options.Kubernetes.Namespace),
            Spec = new V1DeploymentSpec
            {
                // One replica: the nonce cache is in memory, and the runs volume is ReadWriteOnce.
                Replicas = 1,
                Strategy = new V1DeploymentStrategy { Type = "Recreate" },
                Selector = new V1LabelSelector { MatchLabels = Selector() },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = Selector() },
                    Spec = LauncherPod(options, image),
                },
            },
        };
    }

    private static V1PodSpec LauncherPod(SandboxLauncherOptions options, string image) =>
        new V1PodSpec
        {
            ServiceAccountName = LauncherName,
            EnableServiceLinks = false,
            SecurityContext = new V1PodSecurityContext
            {
                RunAsNonRoot = true,
                RunAsUser = 65532,
                RunAsGroup = 65532,
                FsGroup = 65532,
                SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
            },
            Containers =
            [
                new V1Container
                {
                    Name = "launcher",
                    Image = image,
                    Env = LauncherEnv(options),
                    Ports = [new V1ContainerPort { Name = "http", ContainerPort = Port }],
                    ReadinessProbe = new V1Probe
                    {
                        HttpGet = new V1HTTPGetAction { Path = "/healthz", Port = Port },
                    },
                    SecurityContext = new V1SecurityContext
                    {
                        AllowPrivilegeEscalation = false,
                        ReadOnlyRootFilesystem = true,
                        Capabilities = new V1Capabilities { Drop = ["ALL"] },
                    },
                    VolumeMounts =
                    [
                        new V1VolumeMount
                        {
                            Name = KubernetesJobSpec.RunsVolume,
                            MountPath = options.Kubernetes.RunsPath,
                        },
                    ],
                },
            ],
            Volumes =
            [
                new V1Volume
                {
                    Name = KubernetesJobSpec.RunsVolume,
                    PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                    {
                        ClaimName = options.Kubernetes.RunsClaimName,
                    },
                },
            ],
        };

    internal static Dictionary<string, string> Selector() =>
        new(StringComparer.Ordinal) { ["app.kubernetes.io/name"] = LauncherName };

    private static V1ObjectMeta Meta(string name, string? ns) =>
        new()
        {
            Name = name,
            NamespaceProperty = ns,
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app.kubernetes.io/part-of"] = "trax-sandbox",
            },
        };

    private static V1EnvVar Value(string name, string value) =>
        new() { Name = name, Value = value };

    private static V1EnvVar Field(string name, string path) =>
        new()
        {
            Name = name,
            ValueFrom = new V1EnvVarSource
            {
                FieldRef = new V1ObjectFieldSelector { FieldPath = path },
            },
        };
}
