using System.Globalization;
using k8s;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

internal static partial class KubernetesTarget
{
    /// <summary>
    /// The deployment's own services a held pod still reaches, and how: in-cluster Postgres and RabbitMQ by name, an
    /// existing database by the hosts of the connection string in its Secret, and on EKS the foundation's database,
    /// the Pod Identity agent the pod's role comes from, and Secrets Manager.
    /// </summary>
    private sealed record EgressOwn(IReadOnlyList<string> Entries, V1EnvVar? Postgres);

    private sealed partial class Renderer
    {
        /// <summary>The egress image, when any service names egress.</summary>
        public string? EgressImage { get; init; }

        private EgressOwn OwnServices(bool rabbit)
        {
            var own = new List<string>();
            V1EnvVar? postgres = null;
            if (Eks is { } eks)
            {
                own.Add(
                    DeployEgress.Own(
                        eks.DatabaseAddress,
                        int.Parse(eks.DatabasePort, CultureInfo.InvariantCulture)
                    )
                );
                own.Add($"tcp:{DeployEgress.PodIdentityAgent}");
                own.Add(DeployEgress.Own($"secretsmanager.{eks.Region}.amazonaws.com", 443));
            }
            else if (Env.Database is { Secret: { } dbSecret, Key: { } dbKey })
                postgres = FromSecret(DeployEgress.PostgresVariable, dbSecret, dbKey);
            else
                own.Add(DeployEgress.Own(Database, 5432));
            if (rabbit)
                own.Add(DeployEgress.Own(Broker, 5672));
            return new EgressOwn(own, postgres);
        }

        /// <summary>
        /// Holds <paramref name="pod"/> to <paramref name="service"/>'s egress: an init container applies the rules
        /// as root with NET_ADMIN and nothing else, and exits; then a native sidecar (an init container that keeps
        /// running) serves the resolver and the proxy as the egress uid with no capability, and the app starts only
        /// once it answers. The app keeps every capability dropped, so it cannot change the rules. A pod whose
        /// service names no egress is left as it is.
        /// </summary>
        public void HoldEgress(V1PodTemplateSpec pod, DeployService service, bool rabbit)
        {
            if (service.Egress is null)
                return;
            var image =
                EgressImage
                ?? throw new InvalidOperationException("The egress image was not resolved.");
            var own = OwnServices(rabbit);
            var vars = new List<V1EnvVar>
            {
                Value(DeployEgress.AllowVariable, DeployEgress.Allow(service.Egress, own.Entries)),
            };
            if (own.Postgres is not null)
                vars.Add(own.Postgres);

            var rules = RulesContainer(image, vars);
            var sidecar = SidecarContainer(image, vars);
            pod.Spec.InitContainers = [rules, sidecar];
            foreach (var c in pod.Spec.InitContainers)
                pod.Metadata.Annotations[$"{SeccompAnnotation}/{c.Name}"] = "runtime/default";
            pod.Spec.Volumes ??=
            [
                new V1Volume { Name = ScratchVolume, EmptyDir = new V1EmptyDirVolumeSource() },
            ];
        }
    }

    /// <summary>The init container that applies the rules: root, NET_ADMIN and nothing else, and it exits.</summary>
    private static V1Container RulesContainer(string image, List<V1EnvVar> vars)
    {
        var rules = Renderer.Container(
            DeployEgress.RulesContainer,
            image,
            new ServiceHardening(
                ["NET_ADMIN"],
                MemoryMiB: null,
                Pids: null,
                Cpus: null,
                ReadOnlyRoot: true
            ),
            nonRoot: false,
            [.. vars]
        );
        rules.Args = [DeployEgress.RulesCommand];
        rules.SecurityContext.RunAsUser = 0;
        rules.SecurityContext.RunAsGroup = 0;
        return rules;
    }

    /// <summary>
    /// The native sidecar that serves the resolver and the proxy: the egress uid, no capability, and a startup probe
    /// the app's start waits on.
    /// </summary>
    private static V1Container SidecarContainer(string image, List<V1EnvVar> vars)
    {
        var sidecar = Renderer.Container(
            DeployEgress.SidecarContainer,
            image,
            new ServiceHardening([], MemoryMiB: null, Pids: null, Cpus: null, ReadOnlyRoot: true),
            nonRoot: true,
            [.. vars]
        );
        sidecar.Args = [DeployEgress.ServeCommand];
        sidecar.RestartPolicy = "Always";
        sidecar.SecurityContext.RunAsUser = DeployEgress.Uid;
        sidecar.SecurityContext.RunAsGroup = DeployEgress.Uid;
        sidecar.StartupProbe = new V1Probe
        {
            Exec = new V1ExecAction { Command = [DeployEgress.Binary, DeployEgress.ReadyCommand] },
            PeriodSeconds = 1,
            FailureThreshold = 60,
        };
        return sidecar;
    }

    /// <summary>The annotation podman kube play reads a container's seccomp profile from; it ignores the field.</summary>
    public const string SeccompAnnotation = "container.seccomp.security.alpha.kubernetes.io";

    /// <summary>The admission policy and binding that hold a namespace with egress: <c>trax-egress-&lt;namespace&gt;</c>.</summary>
    public static string EgressAdmissionName(string ns) => $"trax-egress-{ns}";

    /// <summary>The capabilities Pod Security's baseline level lets a container add.</summary>
    internal static readonly string[] BaselineCapabilities =
    [
        "AUDIT_WRITE",
        "CHOWN",
        "DAC_OVERRIDE",
        "FOWNER",
        "FSETID",
        "KILL",
        "MKNOD",
        "NET_BIND_SERVICE",
        "SETFCAP",
        "SETGID",
        "SETPCAP",
        "SETUID",
        "SYS_CHROOT",
    ];

    /// <summary>
    /// What holds a namespace with egress in place of Pod Security baseline, which refuses NET_ADMIN on every
    /// container and so on the egress rules too. The policy lets a container add only what baseline allows, except
    /// one init container: named <c>egress-rules</c>, running exactly <paramref name="image"/> (by digest) with its
    /// own entrypoint and the one argument <c>rules</c>, not a sidecar, adding NET_ADMIN and nothing else. It also
    /// refuses a privileged container and the host's network, PID and IPC namespaces, as baseline does, and
    /// <c>runAsUser</c> 61000 (the uid the rules let out) on the pod or on any container but the egress sidecar. It
    /// applies to every pod created or changed in the namespace, an ephemeral container added for debugging included.
    /// <paramref name="previous"/> are egress images the services ran before this deploy: their pods are still
    /// created while the services roll and when they roll back, so the deployer allows them until the roll succeeds.
    /// </summary>
    public static IReadOnlyList<IKubernetesObject<V1ObjectMeta>> EgressAdmission(
        DeployFile file,
        string ns,
        string image,
        IEnumerable<string>? previous = null
    )
    {
        List<string> images =
        [
            image,
            .. (previous ?? [])
                .Where(p => !string.Equals(p, image, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        var name = EgressAdmissionName(ns);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedByLabel] = ManagedBy,
            [AppLabel] = file.App,
            [PartOfLabel] = file.App,
        };
        var policy = new V1ValidatingAdmissionPolicy
        {
            ApiVersion = "admissionregistration.k8s.io/v1",
            Kind = "ValidatingAdmissionPolicy",
            Metadata = new V1ObjectMeta { Name = name, Labels = labels },
            Spec = AdmissionPolicySpec(images),
        };
        var binding = new V1ValidatingAdmissionPolicyBinding
        {
            ApiVersion = "admissionregistration.k8s.io/v1",
            Kind = "ValidatingAdmissionPolicyBinding",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal),
            },
            Spec = new V1ValidatingAdmissionPolicyBindingSpec
            {
                PolicyName = name,
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
        return [policy, binding];
    }

    private static V1ValidatingAdmissionPolicySpec AdmissionPolicySpec(
        IReadOnlyList<string> images
    ) =>
        new V1ValidatingAdmissionPolicySpec
        {
            FailurePolicy = "Fail",
            MatchConstraints = new V1MatchResources
            {
                ResourceRules =
                [
                    new V1NamedRuleWithOperations
                    {
                        ApiGroups = [""],
                        ApiVersions = ["v1"],
                        Operations = ["CREATE", "UPDATE"],
                        Resources = ["pods", "pods/ephemeralcontainers"],
                    },
                ],
            },
            Variables =
            [
                new V1Variable
                {
                    Name = "containers",
                    Expression =
                        "object.spec.containers + (has(object.spec.ephemeralContainers) ? "
                        + "object.spec.ephemeralContainers : [])",
                },
                new V1Variable
                {
                    Name = "inits",
                    Expression =
                        "has(object.spec.initContainers) ? object.spec.initContainers : []",
                },
                new V1Variable
                {
                    Name = "egressImages",
                    Expression = $"[{string.Join(", ", images.Select(i => $"'{i}'"))}]",
                },
                new V1Variable
                {
                    Name = "baseline",
                    Expression =
                        $"[{string.Join(", ", BaselineCapabilities.Select(c => $"'{c}'"))}]",
                },
            ],
            Validations = AdmissionValidations(images[0]),
        };

    /// <summary>A container's added capabilities in CEL, or an empty list when it adds none.</summary>
    private const string Adds =
        "(has(c.securityContext) && has(c.securityContext.capabilities) && "
        + "has(c.securityContext.capabilities.add) ? c.securityContext.capabilities.add : [])";

    /// <summary>
    /// The egress images <paramref name="deployments"/> run their rules with, as they were before a deploy. The
    /// deployer allows them beside the new one while the services roll.
    /// </summary>
    public static IReadOnlyList<string> EgressImagesOf(IEnumerable<V1Deployment?> deployments) =>
        [
            .. deployments
                .SelectMany(d => d?.Spec?.Template?.Spec?.InitContainers ?? [])
                .Where(c =>
                    string.Equals(c.Name, DeployEgress.RulesContainer, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(c.Image)
                )
                .Select(c => c.Image)
                .Distinct(StringComparer.Ordinal),
        ];

    /// <summary>
    /// The admission objects to apply before the services roll: <paramref name="manifests"/>' own, or, when the
    /// services ran another egress image before, the same policy allowing that image too.
    /// </summary>
    public static IReadOnlyList<IKubernetesObject<V1ObjectMeta>> EgressAdmissionWhileRolling(
        DeployFile file,
        KubernetesManifests manifests,
        IReadOnlyList<string> previous
    )
    {
        if (manifests.Admission.Count == 0 || EgressImageOf(file) is not { } image)
            return manifests.Admission;
        return previous.All(p => string.Equals(p, image, StringComparison.Ordinal))
            ? manifests.Admission
            : EgressAdmission(file, manifests.NamespaceName, image, previous);
    }

    /// <summary>The egress uid, as the policy writes it.</summary>
    private static string EgressUid => DeployEgress.Uid.ToString(CultureInfo.InvariantCulture);

    /// <summary>A container that does not ask to run as the egress uid, in CEL.</summary>
    private static string NotEgressUid =>
        "!has(c.securityContext) || !has(c.securityContext.runAsUser) || "
        + $"c.securityContext.runAsUser != {EgressUid}";

    private static List<V1Validation> AdmissionValidations(string image) =>
        [
            new V1Validation
            {
                Expression = $"variables.containers.all(c, {Adds}.all(a, a in variables.baseline))",
                Message =
                    "A container may add only the capabilities Pod Security baseline allows. NET_ADMIN is for the "
                    + "egress rules init container alone.",
            },
            new V1Validation
            {
                Expression =
                    $"variables.inits.all(c, {Adds}.all(a, a in variables.baseline) || "
                    + $"(c.name == '{DeployEgress.RulesContainer}' && c.image in variables.egressImages && "
                    + $"!has(c.command) && has(c.args) && c.args == ['{DeployEgress.RulesCommand}'] && "
                    + $"!has(c.restartPolicy) && {Adds} == ['NET_ADMIN']))",
                Message =
                    $"Only the init container {DeployEgress.RulesContainer}, running {image} with the argument "
                    + "rules, may add NET_ADMIN; any other init container may add only what Pod Security baseline "
                    + "allows.",
            },
            new V1Validation
            {
                Expression =
                    "(variables.containers + variables.inits).all(c, !has(c.securityContext) || "
                    + "!has(c.securityContext.privileged) || c.securityContext.privileged == false)",
                Message = "A privileged container is refused in this namespace.",
            },
            new V1Validation
            {
                Expression =
                    "!(has(object.spec.hostNetwork) && object.spec.hostNetwork) && "
                    + "!(has(object.spec.hostPID) && object.spec.hostPID) && "
                    + "!(has(object.spec.hostIPC) && object.spec.hostIPC)",
                Message =
                    "A pod in this namespace may not share the node's network, PID or IPC namespace.",
            },
            new V1Validation
            {
                Expression =
                    "(!has(object.spec.securityContext) || !has(object.spec.securityContext.runAsUser) || "
                    + $"object.spec.securityContext.runAsUser != {EgressUid}) && "
                    + $"variables.containers.all(c, {NotEgressUid}) && "
                    + $"variables.inits.all(c, {NotEgressUid} || "
                    + $"(c.name == '{DeployEgress.SidecarContainer}' && c.image in variables.egressImages && "
                    + $"!has(c.command) && has(c.args) && c.args == ['{DeployEgress.ServeCommand}'] && "
                    + "has(c.restartPolicy) && c.restartPolicy == 'Always'))",
                Message =
                    $"Only the egress sidecar may run as uid {EgressUid}: the egress rules let that uid out.",
            },
        ];
}
