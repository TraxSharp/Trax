using System.Globalization;
using System.Text.RegularExpressions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Renders a kubernetes environment from trax.deploy.json and what each host reports: one Deployment per service (a
/// hub at one copy that rolls with one new copy beside the old before the old stops, Trax.Cli/docs/adr/0007; workers
/// rolling at their count), a ClusterIP Service for each hub, a migrate Job that runs before any of them, a drain Job
/// and a resume Job the deploy runs around the roll (Trax.Docs/adr/0065), and in-cluster Postgres and RabbitMQ off
/// production. Every container gets <see cref="ServiceHardening"/> as its securityContext and memory limit, and no
/// secret value is written: each comes from a Secret by reference. On a cluster every pod in the namespace refuses
/// ingress but from the app's own pods, and a <c>tailnet</c> exposure adds the edge and the Tailscale operator's
/// Ingress in front of the hub (<see cref="KubernetesTailnet"/>).
/// A service that names egress gets the egress image beside it, and the namespace an admission policy that allows the
/// egress rules their NET_ADMIN and nothing else.
/// </summary>
internal static partial class KubernetesTarget
{
    public const string ManagedByLabel = "app.kubernetes.io/managed-by";
    public const string NameLabel = "app.kubernetes.io/name";
    public const string PartOfLabel = "app.kubernetes.io/part-of";

    /// <summary>The label trax checks before it changes an object: which app it belongs to.</summary>
    public const string AppLabel = "trax/app";

    public const string ManagedBy = "trax";

    /// <summary>The port every Trax host serves HTTP on in its container (ASP.NET Core's default).</summary>
    public const int HttpPort = 8080;

    public const string Database = "database";
    public const string Broker = "rabbitmq";
    public const string MigrateName = "migrate";
    public const string DrainName = "drain";
    public const string ResumeName = "resume";

    /// <summary>
    /// How long a hub's old pod keeps serving after it is marked for termination, before it is sent the stop signal:
    /// ten seconds, time for the Service to stop routing new requests to it.
    /// </summary>
    public const int PreStopSeconds = 10;

    /// <summary>How long a service's rollout may go without progress before the cluster calls it failed: five minutes.</summary>
    public const int RolloutProgressDeadlineSeconds = 300;

    /// <summary>The empty volume a read-only container writes its <c>/tmp</c> to.</summary>
    public const string ScratchVolume = "tmp";

    public const string PostgresPasswordKey = "POSTGRES_PASSWORD";
    public const string RabbitMqPasswordKey = "RABBITMQ_PASSWORD";

    /// <summary>The in-cluster database's volume. A try environment's data, not production's.</summary>
    public const string DatabaseStorage = "1Gi";

    /// <summary>How long a finished migrate Job is kept for its log, in seconds: one day.</summary>
    public const int MigrateJobTtlSeconds = 86_400;

    /// <summary>The label Kubernetes puts on every namespace, holding its name.</summary>
    public const string NamespaceNameLabel = "kubernetes.io/metadata.name";

    /// <summary>The labels the Tailscale operator puts on the proxy pods it runs for an Ingress.</summary>
    public const string OperatorManagedLabel = "tailscale.com/managed";

    public const string OperatorParentTypeLabel = "tailscale.com/parent-resource-type";
    public const string OperatorParentNameLabel = "tailscale.com/parent-resource";
    public const string OperatorParentNamespaceLabel = "tailscale.com/parent-resource-ns";

    /// <summary>The edge's Caddyfile, as a key of its ConfigMap and a file in <see cref="EdgeConfigPath"/>.</summary>
    public const string EdgeConfigFile = "Caddyfile";

    public const string EdgeConfigPath = "/etc/caddy";

    /// <summary>
    /// The edge's container: Caddy as an unprivileged user with every capability dropped and a read-only root,
    /// reading its Caddyfile from <see cref="EdgeConfigPath"/> and the proxy secret from <paramref name="proxySecret"/>.
    /// </summary>
    internal static V1Container EdgeContainer(string proxySecret)
    {
        var container = Renderer.Container(
            KubernetesTailnet.EdgeName,
            KubernetesTailnet.CaddyImage,
            new ServiceHardening(
                [],
                ServiceHardening.MinimumMemoryMiB,
                ServiceHardening.DefaultPids,
                Cpus: null,
                ReadOnlyRoot: true
            ),
            nonRoot: true,
            [
                FromSecret(
                    KubernetesTailnet.EdgeSecretVariable,
                    proxySecret,
                    KubernetesTailnet.ProxySecretKey
                ),
                Value("XDG_CONFIG_HOME", "/tmp/config"),
                Value("XDG_DATA_HOME", "/tmp/data"),
            ]
        );
        container.SecurityContext.RunAsUser = KubernetesTailnet.EdgeUser;
        container.SecurityContext.RunAsGroup = KubernetesTailnet.EdgeUser;
        container.Args =
        [
            "caddy",
            "run",
            "--config",
            $"{EdgeConfigPath}/{EdgeConfigFile}",
            "--adapter",
            "caddyfile",
        ];
        container.Ports =
        [
            new V1ContainerPort { Name = "edge", ContainerPort = KubernetesTailnet.EdgePort },
        ];
        container.VolumeMounts.Add(
            new V1VolumeMount
            {
                Name = "edge-config",
                MountPath = EdgeConfigPath,
                ReadOnlyProperty = true,
            }
        );
        container.ReadinessProbe = new V1Probe
        {
            TcpSocket = new V1TCPSocketAction { Port = KubernetesTailnet.EdgePort },
            PeriodSeconds = 5,
        };
        return container;
    }

    /// <summary>Where the podman sidecar keeps its node key, so a restart is the same machine on the tailnet.</summary>
    public const string TailscaleStatePath = "/var/lib/tailscale";

    /// <summary>Where the podman sidecar reads its serve configuration.</summary>
    public const string TailscaleConfigPath = "/config";

    public const string TailscaleServeFile = "serve.json";

    /// <summary>
    /// The Tailscale sidecar of a podman hub: userspace networking, so it needs no capability and no tun device,
    /// every capability dropped and a read-only root, its auth key from the app's Secret (from <c>.env</c>), and
    /// Tailscale serve proxying HTTPS on the tailnet to the edge on 127.0.0.1. It joins as
    /// <paramref name="hostname"/>.
    /// </summary>
    internal static V1Container TailscaleSidecar(string appSecret, string hostname)
    {
        var container = Renderer.Container(
            "tailscale",
            KubernetesTailnet.TailscaleImage,
            new ServiceHardening(
                [],
                MemoryMiB: 256,
                ServiceHardening.DefaultPids,
                Cpus: null,
                ReadOnlyRoot: true
            ),
            nonRoot: false,
            [
                FromSecret("TS_AUTHKEY", appSecret, KubernetesTailnet.AuthKeySecretKey),
                Value("TS_HOSTNAME", hostname),
                Value("TS_USERSPACE", "true"),
                Value("TS_AUTH_ONCE", "true"),
                Value("TS_ACCEPT_DNS", "false"),
                Value("TS_STATE_DIR", TailscaleStatePath),
                Value("TS_SOCKET", "/tmp/tailscaled.sock"),
                Value("TS_SERVE_CONFIG", $"{TailscaleConfigPath}/{TailscaleServeFile}"),
            ]
        );
        container.VolumeMounts.Add(
            new V1VolumeMount { Name = "tailscale-state", MountPath = TailscaleStatePath }
        );
        container.VolumeMounts.Add(
            new V1VolumeMount
            {
                Name = "tailscale-config",
                MountPath = TailscaleConfigPath,
                ReadOnlyProperty = true,
            }
        );
        return container;
    }

    /// <summary>The volume the edge reads its Caddyfile from, the ConfigMap <paramref name="configMap"/>.</summary>
    internal static V1Volume EdgeConfigVolume(string configMap) =>
        new()
        {
            Name = "edge-config",
            ConfigMap = new V1ConfigMapVolumeSource { Name = configMap },
        };

    public static string NamespaceName(
        DeployFile file,
        string environment,
        DeployEnvironment env
    ) => env.Namespace ?? file.StackName(environment);

    /// <summary>The one Secret the app's secrets and in-cluster passwords are read from: <c>&lt;app&gt;-&lt;env&gt;</c>.</summary>
    public static string SecretName(DeployFile file, string environment) =>
        file.StackName(environment);

    /// <summary>The image a service runs: <c>[&lt;registry&gt;/]&lt;app&gt;-&lt;service&gt;:&lt;tag&gt;</c>.</summary>
    public static string Image(
        DeployFile file,
        DeployEnvironment env,
        string service,
        string tag
    ) =>
        env.Registry is null
            ? $"{ComposeTarget.ImageName(file, service)}:{tag}"
            : $"{env.Registry}/{ComposeTarget.ImageName(file, service)}:{tag}";

    /// <summary>
    /// The migrate Job's name, new on every deploy because a Job cannot be changed:
    /// <c>&lt;app&gt;-migrate-&lt;tag&gt;</c>, made a valid object name of at most 63 characters.
    /// </summary>
    public static string MigrateJobName(DeployFile file, string tag) =>
        JobName(file, MigrateName, tag);

    /// <summary>
    /// A deploy's Job of <paramref name="kind"/> (migrate, drain or resume): <c>&lt;app&gt;-&lt;kind&gt;-&lt;tag&gt;</c>,
    /// new on every deploy because a Job cannot be changed, made a valid object name of at most 63 characters.
    /// </summary>
    public static string JobName(DeployFile file, string kind, string tag)
    {
        var suffix = NotNameChars.Replace(tag.ToLowerInvariant(), "-").Trim('-');
        var name = $"{file.App}-{kind}-{suffix}";
        // A long app name would cut the time off the tag, and with it what makes the name new.
        if (name.Length > 63)
            name = $"{kind}-{suffix}";
        return name[..Math.Min(name.Length, 63)].TrimEnd('-');
    }

    /// <summary>
    /// The environment's manifests. With <paramref name="eks"/>, for a cluster on EKS: images from ECR, a service
    /// account per service for Pod Identity, the database and secrets read by the host from Secrets Manager rather
    /// than from a Kubernetes Secret, and the security group policy that lets the app's pods reach the database.
    /// With <paramref name="force"/>, the drain Job rolls on even when work is still busy at its timeout.
    /// </summary>
    public static KubernetesManifests Render(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        EksTarget? eks = null,
        bool force = false
    )
    {
        var env = KubernetesEnvironment(file, environment);
        ComposeTarget.CheckHosts(file, exports, name => file.Count(env, name));
        var rabbit = exports.Values.Any(e => e.Transports.Contains("rabbitmq"));
        CheckBroker(environment, env, rabbit, eks);
        var inCluster = env.Database is { InCluster: true };
        var ns = NamespaceName(file, environment, env);
        var secret = SecretName(file, environment);
        var tailnet = KubernetesTailnet.Exposed(env);
        var onPodman = RunsOnPodman(env);
        var hub = TailnetHub(file, environment, env, exports);
        var egressImage = EgressImageOf(file);
        var r = new Renderer(
            file,
            env,
            ns,
            secret,
            eks,
            tailnet ? KubernetesTailnet.ProxySecretName(file, environment) : null,
            tag
        )
        {
            EgressImage = egressImage,
        };

        var timeout = env.DrainTimeoutValue();
        var (job, drain, resume) = DeployJobs(
            file,
            r,
            ComposeTarget.Migrator(file, exports),
            tag,
            rabbit,
            timeout,
            force
        );

        var services = Services(file, env, exports, tag, r, rabbit, timeout);
        if (tailnet && !onPodman)
            services.AddRange(
                r.TailnetEdge(KubernetesTailnet.Hostname(file, environment, env), hub.NotNull())
            );
        return new KubernetesManifests(
            NamespaceObject(file, ns),
            egressImage is null || onPodman ? [] : EgressAdmission(file, ns, egressImage),
            onPodman ? [] : r.Policies(hub),
            secret,
            eks is null ? SecretKeys(file, inCluster, rabbit, tailnet && onPodman) : [],
            env.Database is { Secret: { } dbSecret, Key: { } dbKey }
                ? new V1SecretKeySelector { Name = dbSecret, Key = dbKey }
                : null,
            eks is null ? Backing(r, inCluster, rabbit) : r.EksIdentity(eks),
            job,
            drain,
            services,
            resume,
            r.ProxySecret
        );
    }

    /// <summary>The environment, refused when its target is not kubernetes.</summary>
    private static DeployEnvironment KubernetesEnvironment(DeployFile file, string environment)
    {
        var env = file.Environment(environment);
        if (!string.Equals(env.Target, DeployEnvironment.Kubernetes, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Environment '{environment}' has target {env.Target}, not kubernetes."
            );
        return env;
    }

    /// <summary>Whether the environment runs under podman rather than on a cluster.</summary>
    private static bool RunsOnPodman(DeployEnvironment env) =>
        string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal);

    /// <summary>The egress image when any service names egress, else null.</summary>
    private static string? EgressImageOf(DeployFile file) =>
        file.Services.Values.Any(s => s.Egress is not null) ? DeployEgress.Image(file) : null;

    /// <summary>Refuses a broker the environment cannot run: none on production, none on EKS, a limit on none.</summary>
    private static void CheckBroker(
        string environment,
        DeployEnvironment env,
        bool rabbit,
        EksTarget? eks
    )
    {
        if (!rabbit && env.Limits.ContainsKey(DeployFile.BackingBroker))
            throw new InvalidOperationException(
                $"Environment '{environment}': limits names {DeployFile.BackingBroker}, and no host uses RabbitMQ, so "
                    + "the cluster runs none."
            );
        if (
            rabbit
            && string.Equals(env.Profile, DeployEnvironment.Production, StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                $"Environment '{environment}': a host uses RabbitMQ, and the production profile does not run a "
                    + "broker in the cluster, which would be a single copy with no backups. trax cannot name an "
                    + "existing broker yet. Use the try profile, or a transport other than RabbitMQ."
            );
        if (rabbit && eks is not null)
            throw new InvalidOperationException(
                $"Environment '{environment}': a host uses RabbitMQ, and on EKS trax neither runs a broker nor names "
                    + "an existing one yet. Use a transport other than RabbitMQ."
            );
    }

    /// <summary>
    /// The Jobs a deploy runs on the new version's image: the migration, the drain and the resume. Each reaches the
    /// database from inside the cluster, so nothing outside it reaches the app.
    /// </summary>
    private static (V1Job Migrate, V1Job Drain, V1Job Resume) DeployJobs(
        DeployFile file,
        Renderer r,
        string migrator,
        string tag,
        bool rabbit,
        TimeSpan timeout,
        bool force
    )
    {
        var image = r.Image(migrator, tag);
        var hardening = ServiceHardening.Trax(file.Services[migrator]);
        var job = r.TraxJob(
            MigrateName,
            MigrateJobName(file, tag),
            image,
            hardening,
            r.TraxEnv(rabbit),
            [TraxMigrate.Argument]
        );
        List<V1EnvVar> drainEnv =
        [
            .. r.TraxEnv(rabbit),
            Value(TraxMigrate.Variable, TraxMigrate.SkipValue),
        ];
        if (force)
            drainEnv.Add(Value(TraxDrain.ForceVariable, "true"));
        var drain = r.TraxJob(
            DrainName,
            JobName(file, DrainName, tag),
            image,
            hardening,
            drainEnv,
            [TraxDrain.Argument, timeout.ToString("c", CultureInfo.InvariantCulture)]
        );
        var resume = r.TraxJob(
            ResumeName,
            JobName(file, ResumeName, tag),
            image,
            hardening,
            [.. r.TraxEnv(rabbit), Value(TraxMigrate.Variable, TraxMigrate.SkipValue)],
            [TraxDrain.ResumeArgument]
        );
        foreach (var j in new[] { job, drain, resume })
        {
            if (r.OnEks)
                j.Spec.Template.Spec.ServiceAccountName = migrator;
            // Each runs the migrator's code, so each is held to the migrator's egress and still reaches the database.
            r.HoldEgress(j.Spec.Template, file.Services[migrator], rabbit);
        }
        return (job, drain, resume);
    }

    /// <summary>
    /// The hub a tailnet exposure puts on the tailnet, or null when the environment is not exposed. Refuses a host that
    /// signs people in by tailnet identity anywhere else: on an environment that is not exposed on a tailnet, or on a
    /// service that is not the exposed hub. A tailnet exposure serves exactly one hub.
    /// </summary>
    private static string? TailnetHub(
        DeployFile file,
        string environment,
        DeployEnvironment env,
        IReadOnlyDictionary<string, DeployManifest> exports
    )
    {
        if (!KubernetesTailnet.Exposed(env))
        {
            TailnetSignIn.Refuse(
                exports,
                $"environment '{environment}' is not exposed on a tailnet, so nothing proves its requests came "
                    + "through Tailscale."
            );
            return null;
        }
        var hubs = file.Services.Keys.Where(s => ComposeTarget.Hub(exports[s])).ToList();
        if (hubs.Count != 1)
            throw new InvalidOperationException(
                $"Environment '{environment}': a tailnet exposure puts one hub on the tailnet, and this app has "
                    + (hubs.Count == 0 ? "none" : string.Join(", ", hubs))
                    + "."
            );
        TailnetSignIn.Refuse(
            exports,
            $"only the hub '{hubs[0]}' is behind the tailnet proxy in environment '{environment}'.",
            allowed: hubs[0]
        );
        return hubs[0];
    }

    /// <summary>
    /// The keys the app's Secret must hold: the in-cluster passwords it needs, the Tailscale auth key of a podman
    /// sidecar, then the file's secrets.
    /// </summary>
    private static List<string> SecretKeys(
        DeployFile file,
        bool inCluster,
        bool rabbit,
        bool tailscaleSidecar
    )
    {
        var keys = new List<string>();
        if (inCluster)
            keys.Add(PostgresPasswordKey);
        if (rabbit)
            keys.Add(RabbitMqPasswordKey);
        if (tailscaleSidecar)
            keys.Add(KubernetesTailnet.AuthKeySecretKey);
        keys.AddRange(file.Secrets.Order(StringComparer.Ordinal));
        return keys;
    }

    private static V1Namespace NamespaceObject(DeployFile file, string ns) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "Namespace",
            Metadata = new V1ObjectMeta
            {
                Name = ns,
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ManagedByLabel] = ManagedBy,
                    [AppLabel] = file.App,
                },
            },
        };

    /// <summary>The in-cluster Postgres and RabbitMQ, each with the Service in front of it, when the app runs them.</summary>
    private static List<IKubernetesObject<V1ObjectMeta>> Backing(
        Renderer r,
        bool inCluster,
        bool rabbit
    )
    {
        var backing = new List<IKubernetesObject<V1ObjectMeta>>();
        if (inCluster)
        {
            backing.Add(r.HeadlessService(Database, 5432));
            backing.Add(r.Postgres());
        }
        if (rabbit)
        {
            backing.Add(r.Service(Broker, 5672));
            backing.Add(r.RabbitMq());
        }
        return backing;
    }

    /// <summary>Each service's Deployment, with a Service in front of it when it is a hub.</summary>
    private static List<IKubernetesObject<V1ObjectMeta>> Services(
        DeployFile file,
        DeployEnvironment env,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        Renderer r,
        bool rabbit,
        TimeSpan drainTimeout
    )
    {
        var services = new List<IKubernetesObject<V1ObjectMeta>>();
        foreach (var (name, service) in file.Services)
        {
            var export = exports[name];
            var hub = ComposeTarget.Hub(export);
            if (hub)
                services.Add(r.Service(name, HttpPort));
            var deployment = r.Deployment(
                name,
                r.Image(name, tag),
                hub ? 1 : file.Count(env, name),
                hub,
                ServiceHardening.Trax(service),
                [.. r.TraxEnv(rabbit), Value(TraxMigrate.Variable, TraxMigrate.SkipValue)],
                export,
                drainTimeout
            );
            if (r.OnEks)
                deployment.Spec.Template.Spec.ServiceAccountName = name;
            if (hub)
                r.TrustTailnetProxy(deployment);
            r.HoldEgress(deployment.Spec.Template, service, rabbit);
            services.Add(deployment);
        }
        return services;
    }

    /// <summary>The manifests as YAML files, numbered in the order they are applied.</summary>
    public static IReadOnlyList<(string Path, string Content)> Yaml(
        KubernetesManifests manifests
    ) =>
        manifests
            .All.Select(
                (o, i) =>
                    (
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{i:00}-{o.Kind.ToLowerInvariant()}-{o.Metadata.Name}.yaml"
                        ),
                        "# Written by `trax deploy plan` from trax.deploy.json. trax deploy applies these itself.\n"
                            + KubernetesYaml.Serialize(o)
                    )
            )
            .ToList();

    private static V1EnvVar Value(string name, string value) =>
        new() { Name = name, Value = value };

    private static V1EnvVar FromSecret(string name, string secret, string key) =>
        new()
        {
            Name = name,
            ValueFrom = new V1EnvVarSource
            {
                SecretKeyRef = new V1SecretKeySelector { Name = secret, Key = key },
            },
        };

    private sealed partial class Renderer(
        DeployFile file,
        DeployEnvironment env,
        string ns,
        string secret,
        EksTarget? eks,
        string? proxySecret,
        string tag
    )
    {
        public bool OnEks => eks is not null;

        /// <summary>The Secret the proxy secret is held in, when the environment is exposed on a tailnet.</summary>
        public string? ProxySecret => proxySecret;

        private EksTarget? Eks => eks;

        private DeployEnvironment Env => env;

        /// <summary>A service's image: where a deploy to EKS pushed it, else from the environment's registry.</summary>
        public string Image(string service, string tag) =>
            eks is not null ? eks.Images[service] : KubernetesTarget.Image(file, env, service, tag);

        /// <summary>
        /// On EKS: a service account per service, which its Pod Identity association binds the pod role to, and, with
        /// Security Groups for Pods, the policy that gives the app's pods the group the database admits beside the
        /// cluster's own.
        /// </summary>
        public List<IKubernetesObject<V1ObjectMeta>> EksIdentity(EksTarget target)
        {
            var objects = new List<IKubernetesObject<V1ObjectMeta>>();
            foreach (var service in file.Services.Keys)
                objects.Add(
                    new V1ServiceAccount
                    {
                        ApiVersion = "v1",
                        Kind = "ServiceAccount",
                        Metadata = Meta(service),
                        AutomountServiceAccountToken = false,
                    }
                );
            if (target.PodSecurityGroups)
                objects.Add(
                    new EksSecurityGroupPolicy
                    {
                        Metadata = Meta($"{file.App}-database-clients"),
                        Spec = new EksSecurityGroupPolicySpec
                        {
                            PodSelector = new V1LabelSelector
                            {
                                MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    [AppLabel] = file.App,
                                },
                            },
                            SecurityGroups = new EksSecurityGroups
                            {
                                GroupIds =
                                [
                                    target.DatabaseClientSecurityGroup,
                                    target.ClusterSecurityGroup,
                                ],
                            },
                        },
                    }
                );
            return objects;
        }

        /// <summary>
        /// Makes <paramref name="hub"/> the one the tailnet proxy reaches: it is told it is exposed only on a tailnet
        /// and given the proxy's secret, and its pod is marked with this deploy so the secret it reads is this
        /// deploy's. Nothing when the environment is not exposed.
        /// </summary>
        public void TrustTailnetProxy(V1Deployment hub)
        {
            if (proxySecret is null)
                return;
            var container = hub.Spec.Template.Spec.Containers[0];
            container.Env.Add(Value(DeployExposure.Variable, DeployExposure.Tailnet));
            container.Env.Add(
                FromSecret(
                    DeployExposure.ProxySecretVariable,
                    proxySecret,
                    KubernetesTailnet.ProxySecretKey
                )
            );
            Mark(hub.Spec.Template);
        }

        private void Mark(V1PodTemplateSpec template) =>
            template.Metadata.Annotations[KubernetesTailnet.GenerationAnnotation] = tag;

        /// <summary>
        /// The edge on a cluster: its Caddyfile, its Deployment and Service, and the Tailscale operator's Ingress in
        /// front of it, which puts it on the tailnet as <paramref name="hostname"/>.
        /// </summary>
        public List<IKubernetesObject<V1ObjectMeta>> TailnetEdge(string hostname, string hub)
        {
            const string name = KubernetesTailnet.EdgeName;
            var config = new V1ConfigMap
            {
                ApiVersion = "v1",
                Kind = "ConfigMap",
                Metadata = Meta(name),
                Data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [EdgeConfigFile] = KubernetesTailnet.Caddyfile(
                        bind: null,
                        upstream: string.Create(CultureInfo.InvariantCulture, $"{hub}:{HttpPort}")
                    ),
                },
            };
            var template = Pod(name, EdgeContainer(proxySecret.NotNull()));
            template.Metadata.Labels[KubernetesTailnet.RoleLabel] = KubernetesTailnet.EdgeRole;
            template.Spec.Volumes = [.. template.Spec.Volumes ?? [], EdgeConfigVolume(name)];
            Mark(template);
            var meta = Meta(name);
            meta.Labels[KubernetesTailnet.RoleLabel] = KubernetesTailnet.EdgeRole;
            var deployment = new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = meta,
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    ProgressDeadlineSeconds = RolloutProgressDeadlineSeconds,
                    Selector = new V1LabelSelector { MatchLabels = Selector(name) },
                    Template = template,
                },
            };
            var service = Service(name, KubernetesTailnet.EdgePort);
            var ingress = new V1Ingress
            {
                ApiVersion = "networking.k8s.io/v1",
                Kind = "Ingress",
                Metadata = Meta(name),
                Spec = new V1IngressSpec
                {
                    IngressClassName = KubernetesTailnet.IngressClass,
                    DefaultBackend = new V1IngressBackend
                    {
                        Service = new V1IngressServiceBackend
                        {
                            Name = name,
                            Port = new V1ServiceBackendPort { Number = KubernetesTailnet.EdgePort },
                        },
                    },
                    Tls = [new V1IngressTLS { Hosts = [hostname] }],
                },
            };
            return [config, deployment, service, ingress];
        }

        /// <summary>
        /// The namespace's ingress policies: every pod in it refuses every connection by default; the app's pods
        /// (but the edge) accept the app's pods (but the edge); and, on a tailnet, the edge accepts only the Tailscale
        /// operator's proxy pod for this Ingress, and the hub accepts the edge. Egress is not limited here.
        /// </summary>
        public List<V1NetworkPolicy> Policies(string? hub)
        {
            var app = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AppLabel] = file.App,
            };
            V1LabelSelector NotEdge() =>
                new()
                {
                    MatchLabels = new Dictionary<string, string>(app, StringComparer.Ordinal),
                    MatchExpressions =
                    [
                        new V1LabelSelectorRequirement
                        {
                            Key = KubernetesTailnet.RoleLabel,
                            OperatorProperty = "DoesNotExist",
                        },
                    ],
                };
            var policies = new List<V1NetworkPolicy>
            {
                Policy($"{file.App}-deny", new V1LabelSelector(), ingress: []),
                Policy(
                    $"{file.App}-within",
                    NotEdge(),
                    [
                        new V1NetworkPolicyIngressRule
                        {
                            FromProperty = [new V1NetworkPolicyPeer { PodSelector = NotEdge() }],
                        },
                    ]
                ),
            };
            if (proxySecret is not null && hub is not null)
                policies.AddRange(TailnetPolicies(app, hub));
            return policies;
        }

        /// <summary>
        /// The edge accepts only the operator's proxy pod for this Ingress, on the edge's port; the hub accepts the
        /// edge on its own.
        /// </summary>
        private IEnumerable<V1NetworkPolicy> TailnetPolicies(
            Dictionary<string, string> app,
            string hub
        )
        {
            var edge = new Dictionary<string, string>(app, StringComparer.Ordinal)
            {
                [KubernetesTailnet.RoleLabel] = KubernetesTailnet.EdgeRole,
            };
            yield return Policy(
                $"{file.App}-tailnet",
                new V1LabelSelector { MatchLabels = edge },
                [
                    new V1NetworkPolicyIngressRule
                    {
                        FromProperty = [OperatorProxy()],
                        Ports = [Tcp(KubernetesTailnet.EdgePort)],
                    },
                ]
            );
            yield return Policy(
                $"{file.App}-tailnet-hub",
                new V1LabelSelector { MatchLabels = Selector(hub) },
                [
                    new V1NetworkPolicyIngressRule
                    {
                        FromProperty =
                        [
                            new V1NetworkPolicyPeer
                            {
                                PodSelector = new V1LabelSelector { MatchLabels = edge },
                            },
                        ],
                        Ports = [Tcp(HttpPort)],
                    },
                ]
            );
        }

        /// <summary>
        /// The Tailscale operator's proxy pod for this environment's Ingress, by the labels the operator puts on it, in
        /// the operator's namespace.
        /// </summary>
        private V1NetworkPolicyPeer OperatorProxy() =>
            new()
            {
                NamespaceSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [NamespaceNameLabel] = KubernetesTailnet.OperatorNamespace(env),
                    },
                },
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [OperatorManagedLabel] = "true",
                        [OperatorParentTypeLabel] = "ingress",
                        [OperatorParentNameLabel] = KubernetesTailnet.EdgeName,
                        [OperatorParentNamespaceLabel] = ns,
                    },
                },
            };

        private static V1NetworkPolicyPort Tcp(int port) => new() { Protocol = "TCP", Port = port };

        private V1NetworkPolicy Policy(
            string name,
            V1LabelSelector pods,
            IList<V1NetworkPolicyIngressRule> ingress
        ) =>
            new()
            {
                ApiVersion = "networking.k8s.io/v1",
                Kind = "NetworkPolicy",
                Metadata = Meta(name),
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = pods,
                    PolicyTypes = ["Ingress"],
                    Ingress = ingress,
                },
            };

        public Dictionary<string, string> Labels(string name) =>
            new(StringComparer.Ordinal)
            {
                [NameLabel] = name,
                [PartOfLabel] = file.App,
                [ManagedByLabel] = ManagedBy,
                [AppLabel] = file.App,
            };

        private Dictionary<string, string> Selector(string name) =>
            new(StringComparer.Ordinal) { [NameLabel] = name, [AppLabel] = file.App };

        private V1ObjectMeta Meta(string name) =>
            new()
            {
                Name = name,
                NamespaceProperty = ns,
                Labels = Labels(name),
            };

        /// <summary>
        /// What every Trax container is given: its database, its broker, the settings by value and the secrets by
        /// reference to the environment's Secret.
        /// </summary>
        public List<V1EnvVar> TraxEnv(bool rabbit)
        {
            if (eks is not null)
                return EksEnv(eks);
            var vars = new List<V1EnvVar>();
            var connection = $"ConnectionStrings__{file.ConnectionStrings.Trax}";
            if (env.Database is { Secret: { } dbSecret, Key: { } dbKey })
                vars.Add(FromSecret(connection, dbSecret, dbKey));
            else
            {
                // As on compose: no password in the connection string, where a ';' in it would break it.
                vars.Add(
                    Value(
                        connection,
                        $"Host={Database};Database={file.App};Username={file.App};GSS Encryption Mode=Disable"
                    )
                );
                vars.Add(FromSecret("PGPASSWORD", secret, PostgresPasswordKey));
            }
            if (rabbit)
            {
                var broker = file.ConnectionStrings.RabbitMq;
                vars.Add(Value($"{broker}__Host", Broker));
                vars.Add(Value($"{broker}__UserName", file.App));
                vars.Add(FromSecret($"{broker}__Password", secret, RabbitMqPasswordKey));
            }
            foreach (var name in file.Settings.Order(StringComparer.Ordinal))
                vars.Add(Value(name, env.Settings[name]));
            foreach (var name in file.Secrets.Order(StringComparer.Ordinal))
                vars.Add(FromSecret(name, secret, name));
            return vars;
        }

        /// <summary>
        /// What a Trax container is given on EKS: the foundation's database by address with no password, and the
        /// entries <c>AddTraxAwsSecretsAsync</c> follows to read the password and each app secret from Secrets
        /// Manager with the pod's role. No value is in the manifest.
        /// </summary>
        private List<V1EnvVar> EksEnv(EksTarget target)
        {
            var trax = file.ConnectionStrings.Trax;
            var vars = new List<V1EnvVar>
            {
                Value(
                    $"ConnectionStrings__{trax}",
                    $"Host={target.DatabaseAddress};Port={target.DatabasePort};Database={AwsStack.DatabaseName};"
                        + $"Username={AwsStack.DatabaseUser};SSL Mode=VerifyFull;Root Certificate={AwsStack.RdsCaBundle};"
                        + "GSS Encryption Mode=Disable"
                ),
                Value("AWS_REGION", target.Region),
            };
            var entries = new List<(string SecretId, string? JsonKey, string Field, string Target)>
            {
                (target.DatabaseSecretArn, "password", "PasswordOf", trax),
            };
            entries.AddRange(
                file.Secrets.Order(StringComparer.Ordinal)
                    .Select(name =>
                        (AwsStack.AppSecretName(secret, name), (string?)null, "Key", name)
                    )
            );
            for (var i = 0; i < entries.Count; i++)
            {
                var prefix = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Trax__AwsSecrets__{i}__"
                );
                var (id, jsonKey, field, value) = entries[i];
                vars.Add(Value($"{prefix}SecretId", id));
                if (jsonKey is not null)
                    vars.Add(Value($"{prefix}JsonKey", jsonKey));
                vars.Add(Value($"{prefix}{field}", value));
            }
            foreach (var name in file.Settings.Order(StringComparer.Ordinal))
                vars.Add(Value(name, env.Settings[name]));
            return vars;
        }

        /// <summary>
        /// A container under <paramref name="hardening"/>: every capability dropped and only its own added back, no
        /// privilege gained, the runtime's seccomp profile, its memory and CPU as both request and limit when they are
        /// set (none when they are not, which makes the pod BestEffort, the first evicted when its node runs short),
        /// and a read-only root with an empty <c>/tmp</c> when the hardening asks for one. Kubernetes has no
        /// per-container pids limit, so <see cref="ServiceHardening.Pids"/> is the node's to enforce.
        /// </summary>
        public static V1Container Container(
            string name,
            string image,
            ServiceHardening hardening,
            bool nonRoot,
            IList<V1EnvVar> vars
        )
        {
            var resources = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal);
            if (hardening.MemoryMiB is { } memory)
                resources["memory"] = new ResourceQuantity(
                    string.Create(CultureInfo.InvariantCulture, $"{memory}Mi")
                );
            if (hardening.Cpus is { } cpus)
                resources["cpu"] = new ResourceQuantity(
                    string.Create(CultureInfo.InvariantCulture, $"{(int)(cpus * 1000)}m")
                );
            return new V1Container
            {
                Name = name,
                Image = image,
                ImagePullPolicy = "IfNotPresent",
                Env = vars,
                SecurityContext = new V1SecurityContext
                {
                    RunAsNonRoot = nonRoot,
                    AllowPrivilegeEscalation = false,
                    ReadOnlyRootFilesystem = hardening.ReadOnlyRoot ? true : null,
                    Capabilities = new V1Capabilities
                    {
                        Drop = ["ALL"],
                        Add =
                            hardening.AddCapabilities.Count > 0
                                ? [.. hardening.AddCapabilities]
                                : null,
                    },
                    SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
                },
                VolumeMounts = hardening.ReadOnlyRoot
                    ? [new V1VolumeMount { Name = ScratchVolume, MountPath = "/tmp" }]
                    : null,
                Resources =
                    resources.Count == 0
                        ? null
                        : new V1ResourceRequirements
                        {
                            Limits = resources,
                            Requests = new Dictionary<string, ResourceQuantity>(
                                resources,
                                StringComparer.Ordinal
                            ),
                        },
            };
        }

        /// <summary>
        /// A pod of one container, in a user namespace of its own (<c>hostUsers: false</c>), with no service account
        /// token and no service links. The seccomp annotation beside <c>seccompProfile</c> is the one podman kube
        /// play reads; it ignores the field.
        /// </summary>
        public V1PodTemplateSpec Pod(string name, V1Container container, string? restart = null) =>
            new()
            {
                Metadata = new V1ObjectMeta
                {
                    Labels = Labels(name),
                    Annotations = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [$"{SeccompAnnotation}/{container.Name}"] = "runtime/default",
                    },
                },
                Spec = new V1PodSpec
                {
                    HostUsers = false,
                    AutomountServiceAccountToken = false,
                    EnableServiceLinks = false,
                    RestartPolicy = restart,
                    Containers = [container],
                    Volumes = container.VolumeMounts?.Any(m =>
                        string.Equals(m.Name, ScratchVolume, StringComparison.Ordinal)
                    )
                        is true
                        ?
                        [
                            new V1Volume
                            {
                                Name = ScratchVolume,
                                EmptyDir = new V1EmptyDirVolumeSource(),
                            },
                        ]
                        : null,
                },
            };

        public V1Deployment Deployment(
            string name,
            string image,
            int replicas,
            bool hub,
            ServiceHardening hardening,
            IList<V1EnvVar> vars,
            DeployManifest export,
            TimeSpan drainTimeout
        )
        {
            var container = Container(name, image, hardening, nonRoot: true, vars);
            if (export.Endpoints.GraphQL is not null || export.Endpoints.Health is not null)
                container.Ports = [new V1ContainerPort { Name = "http", ContainerPort = HttpPort }];
            if (export.Endpoints.Health is { } health)
            {
                container.ReadinessProbe = new V1Probe
                {
                    HttpGet = new V1HTTPGetAction { Path = health, Port = HttpPort },
                    PeriodSeconds = 5,
                };
                container.LivenessProbe = new V1Probe
                {
                    HttpGet = new V1HTTPGetAction { Path = health, Port = HttpPort },
                    InitialDelaySeconds = 10,
                    PeriodSeconds = 10,
                };
            }
            if (hub)
                container.Lifecycle = PreStop();
            var pod = Pod(name, container);
            // After the drain nothing is running, so a stopping host exits at once. A deploy with --force rolls with
            // work still running: the stop gives it the drain's timeout again to finish, past the preStop's sleep.
            pod.Spec.TerminationGracePeriodSeconds =
                (long)drainTimeout.TotalSeconds + PreStopSeconds;
            return new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = Meta(name),
                Spec = new V1DeploymentSpec
                {
                    Replicas = replicas,
                    // A hub's next version starts beside the old one and takes traffic once ready, and only then does
                    // the old one stop. Two hubs at once are safe because the deploy drained first
                    // (Trax.Cli/docs/adr/0007).
                    Strategy = new V1DeploymentStrategy
                    {
                        Type = "RollingUpdate",
                        RollingUpdate = hub
                            ? new V1RollingUpdateDeployment { MaxSurge = 1, MaxUnavailable = 0 }
                            : null,
                    },
                    // The cluster marks a rollout that stops making progress for this long as failed, which ends
                    // the deploy's wait early and rolls it back.
                    ProgressDeadlineSeconds = RolloutProgressDeadlineSeconds,
                    Selector = new V1LabelSelector { MatchLabels = Selector(name) },
                    Template = pod,
                },
            };
        }

        /// <summary>
        /// The old pod keeps serving while the Service stops routing to it, then gets the stop signal. The kubelet
        /// sleeps, so the image needs no shell.
        /// </summary>
        private static V1Lifecycle PreStop() =>
            new()
            {
                PreStop = new V1LifecycleHandler
                {
                    Sleep = new V1SleepAction { Seconds = PreStopSeconds },
                },
            };

        public V1Service Service(string name, int port) =>
            new()
            {
                ApiVersion = "v1",
                Kind = "Service",
                Metadata = Meta(name),
                Spec = new V1ServiceSpec
                {
                    Type = "ClusterIP",
                    Selector = Selector(name),
                    Ports = [new V1ServicePort { Port = port, TargetPort = port }],
                },
            };

        public V1Service HeadlessService(string name, int port)
        {
            var service = Service(name, port);
            service.Spec.ClusterIP = "None";
            return service;
        }

        /// <summary>
        /// A Job that runs the app's image once with <paramref name="args"/> (the migration, the drain or the resume),
        /// never retried: a second try would hide why the first failed.
        /// </summary>
        public V1Job TraxJob(
            string kind,
            string name,
            string image,
            ServiceHardening hardening,
            IList<V1EnvVar> vars,
            IList<string> args
        )
        {
            var container = Container(kind, image, hardening, nonRoot: true, vars);
            container.Args = args;
            var meta = Meta(name);
            meta.Labels[NameLabel] = kind;
            var pod = Pod(kind, container, restart: "Never");
            return new V1Job
            {
                ApiVersion = "batch/v1",
                Kind = "Job",
                Metadata = meta,
                Spec = new V1JobSpec
                {
                    BackoffLimit = 0,
                    TtlSecondsAfterFinished = MigrateJobTtlSeconds,
                    Template = pod,
                },
            };
        }

        /// <summary>
        /// Postgres 17 as a StatefulSet of one, for a try environment. Its entrypoint starts as root to take its data
        /// directory and drops to the postgres user, so it runs with <see cref="ServiceHardening.Database"/>'s
        /// capabilities and without runAsNonRoot.
        /// </summary>
        public V1StatefulSet Postgres() =>
            new()
            {
                ApiVersion = "apps/v1",
                Kind = "StatefulSet",
                Metadata = Meta(Database),
                Spec = new V1StatefulSetSpec
                {
                    Replicas = 1,
                    ServiceName = Database,
                    Selector = new V1LabelSelector { MatchLabels = Selector(Database) },
                    Template = Pod(Database, PostgresContainer()),
                    VolumeClaimTemplates = [DataClaim()],
                },
            };

        private V1Container PostgresContainer()
        {
            var container = Container(
                Database,
                "postgres:17",
                ServiceHardening.Database(env.Limits.GetValueOrDefault(DeployFile.BackingDatabase)),
                nonRoot: false,
                [
                    Value("POSTGRES_DB", file.App),
                    Value("POSTGRES_USER", file.App),
                    FromSecret("POSTGRES_PASSWORD", secret, PostgresPasswordKey),
                    // The volume's root holds lost+found, which initdb refuses.
                    Value("PGDATA", "/var/lib/postgresql/data/pgdata"),
                ]
            );
            container.Ports = [new V1ContainerPort { Name = "postgres", ContainerPort = 5432 }];
            container.VolumeMounts =
            [
                new V1VolumeMount { Name = "data", MountPath = "/var/lib/postgresql/data" },
            ];
            container.ReadinessProbe = new V1Probe
            {
                Exec = new V1ExecAction { Command = ["pg_isready", "-U", file.App] },
                PeriodSeconds = 5,
            };
            return container;
        }

        private V1PersistentVolumeClaim DataClaim() =>
            new()
            {
                Metadata = new V1ObjectMeta { Name = "data", Labels = Labels(Database) },
                Spec = new V1PersistentVolumeClaimSpec
                {
                    AccessModes = ["ReadWriteOnce"],
                    Resources = new V1VolumeResourceRequirements
                    {
                        Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                        {
                            ["storage"] = new ResourceQuantity(DatabaseStorage),
                        },
                    },
                },
            };

        /// <summary>RabbitMQ 4 as a Deployment of one, for a try environment, with no data kept across restarts.</summary>
        public V1Deployment RabbitMq()
        {
            var container = Container(
                Broker,
                "rabbitmq:4",
                ServiceHardening.Broker(env.Limits.GetValueOrDefault(DeployFile.BackingBroker)),
                nonRoot: false,
                [
                    Value("RABBITMQ_DEFAULT_USER", file.App),
                    FromSecret("RABBITMQ_DEFAULT_PASS", secret, RabbitMqPasswordKey),
                ]
            );
            container.Ports = [new V1ContainerPort { Name = "amqp", ContainerPort = 5672 }];
            container.ReadinessProbe = new V1Probe
            {
                Exec = new V1ExecAction { Command = ["rabbitmq-diagnostics", "-q", "ping"] },
                PeriodSeconds = 10,
                TimeoutSeconds = 10,
            };
            return new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = Meta(Broker),
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Strategy = new V1DeploymentStrategy { Type = "Recreate" },
                    Selector = new V1LabelSelector { MatchLabels = Selector(Broker) },
                    Template = Pod(Broker, container),
                },
            };
        }
    }

    [GeneratedRegex("[^a-z0-9-]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotNameChars { get; }
}
