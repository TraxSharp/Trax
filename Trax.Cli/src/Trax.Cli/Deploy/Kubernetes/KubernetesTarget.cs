using System.Text.RegularExpressions;
using k8s;
using k8s.Models;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a kubernetes environment is made of, in the order the deployer applies it: the namespace, the in-cluster
/// Postgres and RabbitMQ when the environment runs them, the migrate Job, then each service's Service and Deployment.
/// </summary>
internal sealed record KubernetesManifests(
    V1Namespace Namespace,
    string SecretName,
    IReadOnlyList<string> SecretKeys,
    V1SecretKeySelector? DatabaseSecret,
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Backing,
    V1Job Migrate,
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Services
)
{
    public string NamespaceName => Namespace.Metadata.Name;

    /// <summary>Every object, in the order it is applied.</summary>
    public IEnumerable<IKubernetesObject<V1ObjectMeta>> All =>
        [Namespace, .. Backing, Migrate, .. Services];
}

/// <summary>
/// Renders a kubernetes environment from trax.deploy.json and what each host reports: one Deployment per service (a
/// hub at one copy with <c>Recreate</c>, so two never run at once; workers rolling at their count), a ClusterIP
/// Service for each hub, a migrate Job that runs before any of them, and in-cluster Postgres and RabbitMQ off
/// production. Every container gets <see cref="ServiceHardening"/> as its securityContext and memory limit, and no
/// secret value is written: each comes from a Secret by reference.
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

    public const string PostgresPasswordKey = "POSTGRES_PASSWORD";
    public const string RabbitMqPasswordKey = "RABBITMQ_PASSWORD";

    /// <summary>The in-cluster database's volume. A try environment's data, not production's.</summary>
    public const string DatabaseStorage = "1Gi";

    /// <summary>How long a finished migrate Job is kept for its log, in seconds: one day.</summary>
    public const int MigrateJobTtlSeconds = 86_400;

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
    public static string MigrateJobName(DeployFile file, string tag)
    {
        var suffix = NotNameChars().Replace(tag.ToLowerInvariant(), "-").Trim('-');
        var name = $"{file.App}-{MigrateName}-{suffix}";
        // A long app name would cut the time off the tag, and with it what makes the name new.
        if (name.Length > 63)
            name = $"{MigrateName}-{suffix}";
        return name[..Math.Min(name.Length, 63)].TrimEnd('-');
    }

    public static KubernetesManifests Render(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag
    )
    {
        var env = file.Environment(environment);
        if (env.Target != DeployEnvironment.Kubernetes)
            throw new InvalidOperationException(
                $"Environment '{environment}' has target {env.Target}, not kubernetes."
            );
        ComposeTarget.CheckHosts(file, exports, name => file.Count(env, name));
        var rabbit = exports.Values.Any(e => e.Transports.Contains("rabbitmq"));
        if (rabbit && env.Profile == DeployEnvironment.Production)
            throw new InvalidOperationException(
                $"Environment '{environment}': a host uses RabbitMQ, and the production profile does not run a "
                    + "broker in the cluster, which would be a single copy with no backups. trax cannot name an "
                    + "existing broker yet. Use the try profile, or a transport other than RabbitMQ."
            );
        var inCluster = env.Database is { InCluster: true };
        var ns = NamespaceName(file, environment, env);
        var secret = SecretName(file, environment);

        var keys = new List<string>();
        if (inCluster)
            keys.Add(PostgresPasswordKey);
        if (rabbit)
            keys.Add(RabbitMqPasswordKey);
        keys.AddRange(file.Secrets.Order(StringComparer.Ordinal));

        var r = new Renderer(file, env, ns, secret);
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

        var migrator = ComposeTarget.Migrator(file, exports);
        var job = r.MigrateJob(
            MigrateJobName(file, tag),
            Image(file, env, migrator, tag),
            ServiceHardening.Trax(file.Services[migrator]),
            r.TraxEnv(rabbit)
        );

        var services = new List<IKubernetesObject<V1ObjectMeta>>();
        foreach (var (name, service) in file.Services)
        {
            var export = exports[name];
            var hub = ComposeTarget.Hub(export);
            if (hub)
                services.Add(r.Service(name, HttpPort));
            services.Add(
                r.Deployment(
                    name,
                    Image(file, env, name, tag),
                    hub ? 1 : file.Count(env, name),
                    hub,
                    ServiceHardening.Trax(service),
                    [.. r.TraxEnv(rabbit), Value(TraxMigrate.Variable, TraxMigrate.SkipValue)],
                    export
                )
            );
        }

        return new KubernetesManifests(
            new V1Namespace
            {
                ApiVersion = "v1",
                Kind = "Namespace",
                Metadata = new V1ObjectMeta
                {
                    Name = ns,
                    Labels = new Dictionary<string, string>
                    {
                        [ManagedByLabel] = ManagedBy,
                        [AppLabel] = file.App,
                    },
                },
            },
            secret,
            keys,
            env.Database is { Secret: { } dbSecret, Key: { } dbKey }
                ? new V1SecretKeySelector { Name = dbSecret, Key = dbKey }
                : null,
            backing,
            job,
            services
        );
    }

    /// <summary>The manifests as YAML files, numbered in the order they are applied.</summary>
    public static IReadOnlyList<(string Path, string Content)> Yaml(
        KubernetesManifests manifests
    ) =>
        manifests
            .All.Select(
                (o, i) =>
                    (
                        $"{i:00}-{o.Kind.ToLowerInvariant()}-{o.Metadata.Name}.yaml",
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

    private sealed class Renderer(DeployFile file, DeployEnvironment env, string ns, string secret)
    {
        public Dictionary<string, string> Labels(string name) =>
            new()
            {
                [NameLabel] = name,
                [PartOfLabel] = file.App,
                [ManagedByLabel] = ManagedBy,
                [AppLabel] = file.App,
            };

        private Dictionary<string, string> Selector(string name) =>
            new() { [NameLabel] = name, [AppLabel] = file.App };

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
        /// A container under <paramref name="hardening"/>: every capability dropped and only its own added back, no
        /// privilege gained, the runtime's seccomp profile, and its memory as both request and limit. Kubernetes has
        /// no per-container pids limit, so <see cref="ServiceHardening.Pids"/> is the node's to enforce.
        /// </summary>
        public static V1Container Container(
            string name,
            string image,
            ServiceHardening hardening,
            bool nonRoot,
            IList<V1EnvVar> vars
        )
        {
            var memory = new Dictionary<string, ResourceQuantity>
            {
                ["memory"] = new ResourceQuantity($"{hardening.MemoryMiB}Mi"),
            };
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
                Resources = new V1ResourceRequirements
                {
                    Limits = memory,
                    Requests = new Dictionary<string, ResourceQuantity>(memory),
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
                    Annotations = new Dictionary<string, string>
                    {
                        [$"container.seccomp.security.alpha.kubernetes.io/{container.Name}"] =
                            "runtime/default",
                    },
                },
                Spec = new V1PodSpec
                {
                    HostUsers = false,
                    AutomountServiceAccountToken = false,
                    EnableServiceLinks = false,
                    RestartPolicy = restart,
                    Containers = [container],
                },
            };

        public V1Deployment Deployment(
            string name,
            string image,
            int replicas,
            bool hub,
            ServiceHardening hardening,
            IList<V1EnvVar> vars,
            DeployManifest export
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
            return new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = Meta(name),
                Spec = new V1DeploymentSpec
                {
                    Replicas = replicas,
                    // A hub stops before its next version starts: two hubs never run at once.
                    Strategy = new V1DeploymentStrategy
                    {
                        Type = hub ? "Recreate" : "RollingUpdate",
                    },
                    Selector = new V1LabelSelector { MatchLabels = Selector(name) },
                    Template = Pod(name, container),
                },
            };
        }

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

        public V1Job MigrateJob(
            string name,
            string image,
            ServiceHardening hardening,
            IList<V1EnvVar> vars
        )
        {
            var container = Container(MigrateName, image, hardening, nonRoot: true, vars);
            container.Args = [TraxMigrate.Argument];
            var meta = Meta(name);
            meta.Labels[NameLabel] = MigrateName;
            var pod = Pod(MigrateName, container, restart: "Never");
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
        public V1StatefulSet Postgres()
        {
            var container = Container(
                Database,
                "postgres:17",
                ServiceHardening.Database,
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
            return new V1StatefulSet
            {
                ApiVersion = "apps/v1",
                Kind = "StatefulSet",
                Metadata = Meta(Database),
                Spec = new V1StatefulSetSpec
                {
                    Replicas = 1,
                    ServiceName = Database,
                    Selector = new V1LabelSelector { MatchLabels = Selector(Database) },
                    Template = Pod(Database, container),
                    VolumeClaimTemplates =
                    [
                        new V1PersistentVolumeClaim
                        {
                            Metadata = new V1ObjectMeta
                            {
                                Name = "data",
                                Labels = Labels(Database),
                            },
                            Spec = new V1PersistentVolumeClaimSpec
                            {
                                AccessModes = ["ReadWriteOnce"],
                                Resources = new V1VolumeResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["storage"] = new ResourceQuantity(DatabaseStorage),
                                    },
                                },
                            },
                        },
                    ],
                },
            };
        }

        /// <summary>RabbitMQ 4 as a Deployment of one, for a try environment, with no data kept across restarts.</summary>
        public V1Deployment RabbitMq()
        {
            var container = Container(
                Broker,
                "rabbitmq:4",
                ServiceHardening.Broker,
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

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex NotNameChars();
}
