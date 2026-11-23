using System.Globalization;
using System.Text.RegularExpressions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy.Aws;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

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
        var suffix = NotNameChars.Replace(tag.ToLowerInvariant(), "-").Trim('-');
        var name = $"{file.App}-{MigrateName}-{suffix}";
        // A long app name would cut the time off the tag, and with it what makes the name new.
        if (name.Length > 63)
            name = $"{MigrateName}-{suffix}";
        return name[..Math.Min(name.Length, 63)].TrimEnd('-');
    }

    /// <summary>
    /// The environment's manifests. With <paramref name="eks"/>, for a cluster on EKS: images from ECR, a service
    /// account per service for Pod Identity, the database and secrets read by the host from Secrets Manager rather
    /// than from a Kubernetes Secret, and the security group policy that lets the app's pods reach the database.
    /// </summary>
    public static KubernetesManifests Render(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        EksTarget? eks = null
    )
    {
        var env = file.Environment(environment);
        if (!string.Equals(env.Target, DeployEnvironment.Kubernetes, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Environment '{environment}' has target {env.Target}, not kubernetes."
            );
        ComposeTarget.CheckHosts(file, exports, name => file.Count(env, name));
        var rabbit = exports.Values.Any(e => e.Transports.Contains("rabbitmq"));
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
        var inCluster = env.Database is { InCluster: true };
        var ns = NamespaceName(file, environment, env);
        var secret = SecretName(file, environment);
        var r = new Renderer(file, env, ns, secret, eks);

        var migrator = ComposeTarget.Migrator(file, exports);
        var job = r.MigrateJob(
            MigrateJobName(file, tag),
            r.Image(migrator, tag),
            ServiceHardening.Trax(file.Services[migrator]),
            r.TraxEnv(rabbit)
        );
        if (eks is not null)
            job.Spec.Template.Spec.ServiceAccountName = migrator;

        return new KubernetesManifests(
            NamespaceObject(file, ns),
            secret,
            eks is null ? SecretKeys(file, inCluster, rabbit) : [],
            env.Database is { Secret: { } dbSecret, Key: { } dbKey }
                ? new V1SecretKeySelector { Name = dbSecret, Key = dbKey }
                : null,
            eks is null ? Backing(r, inCluster, rabbit) : r.EksIdentity(eks),
            job,
            Services(file, env, exports, tag, r, rabbit)
        );
    }

    /// <summary>The keys the app's Secret must hold: the in-cluster passwords it needs, then the file's secrets.</summary>
    private static List<string> SecretKeys(DeployFile file, bool inCluster, bool rabbit)
    {
        var keys = new List<string>();
        if (inCluster)
            keys.Add(PostgresPasswordKey);
        if (rabbit)
            keys.Add(RabbitMqPasswordKey);
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
        bool rabbit
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
                export
            );
            if (r.OnEks)
                deployment.Spec.Template.Spec.ServiceAccountName = name;
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

    private sealed class Renderer(
        DeployFile file,
        DeployEnvironment env,
        string ns,
        string secret,
        EksTarget? eks
    )
    {
        public bool OnEks => eks is not null;

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
            var memory = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
            {
                ["memory"] = new ResourceQuantity(
                    string.Create(CultureInfo.InvariantCulture, $"{hardening.MemoryMiB}Mi")
                ),
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
                    Requests = new Dictionary<string, ResourceQuantity>(
                        memory,
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

    [GeneratedRegex("[^a-z0-9-]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotNameChars { get; }
}
