using System.Globalization;
using System.Text;
using k8s;
using k8s.Models;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Renders a kubernetes environment with <c>"runtime": "podman"</c> for one host: the manifests a cluster would get,
/// in the subset <c>podman kube play</c> runs, each pod under a Quadlet <c>.kube</c> unit on a network of the app's
/// own. What only a cluster reads is left out: the Namespace (the network separates apps), the Services (each
/// container answers by its own name on that network, the name a Service gives it on a cluster), and
/// <c>hostUsers</c> (rootless podman cannot map it; every unit runs <c>UserNS=nomap</c>, as compose does). Postgres
/// is a Deployment of one with a claim, since <c>kube play</c> has no StatefulSet. The migration runs in the
/// migrator's unit before its pod starts and fails the unit unless it exits 0, and every other service starts after
/// it. The drain and resume Jobs are written as pods the install runs around a restart of the app's units
/// (<see cref="Commands.DeployKubernetesCommands.InstallHint"/>), since systemd stops a unit before its
/// <c>ExecStartPre</c> runs. No secret value is written here: the Secrets are filled from <c>.env</c> by the deploy,
/// into a file outside the project.
/// </summary>
internal static class PodmanTarget
{
    /// <summary>The podman binary the units call, where Quadlet itself finds it.</summary>
    public const string Podman = "/usr/bin/podman";

    /// <summary>util-linux's flock, which every play of the stack's Secrets runs under.</summary>
    public const string Flock = "/usr/bin/flock";

    /// <summary>How long a unit may take to start, the migration included: thirty minutes, as on a cluster.</summary>
    public const int StartTimeoutSeconds = 1800;

    /// <summary>
    /// The kinds trax writes for <c>podman kube play</c>: the ones it runs from podman 4.9 (Ubuntu 24.04's) on. A Job
    /// is left out, since it plays only from podman 5.
    /// </summary>
    public static readonly string[] PlayableKinds =
    [
        "Pod",
        "Deployment",
        "PersistentVolumeClaim",
        "ConfigMap",
        "Secret",
        "DaemonSet",
    ];

    /// <summary>Where a deploy writes the files a host installs: <c>.trax/deploy/podman/&lt;app&gt;-&lt;env&gt;/</c>.</summary>
    public static string DeployDirectory(string projectRoot, string stack) =>
        Path.Combine(projectRoot, ".trax", "deploy", "podman", stack);

    /// <summary>Where a deploy writes the Secrets with their values: <c>&lt;config&gt;/trax/&lt;app&gt;-&lt;env&gt;/secret.yaml</c>.</summary>
    public static string SecretPath(string configHome, string stack) =>
        Path.Combine(configHome, "trax", stack, "secret.yaml");

    /// <summary>
    /// Where a deploy writes a tailnet exposure's proxy secret, beside the Secrets: a file of its own, played only by
    /// the hub's unit, so rotating it on every deploy restarts the hub and nothing else.
    /// </summary>
    public static string ProxySecretPath(string secretPath) =>
        Path.Combine(Path.GetDirectoryName(secretPath) ?? ".", "tailnet.yaml");

    /// <summary>
    /// Where a deploy writes the new proxy secret: beside <see cref="ProxySecretPath"/>, which the release's restart
    /// step replaces with it. A unit that restarts on its own before then still plays the value its peer runs with.
    /// </summary>
    public static string NextProxySecretPath(string secretPath) =>
        Path.Combine(Path.GetDirectoryName(secretPath) ?? ".", "tailnet.next.yaml");

    /// <summary>The lock every play of the stack's Secrets holds, beside them.</summary>
    public static string SecretLockPath(string secretPath) =>
        Path.Combine(Path.GetDirectoryName(secretPath) ?? ".", "secret.lock");

    /// <summary>
    /// Plays <paramref name="file"/>, a file of the stack's Secrets, under <see cref="SecretLockPath"/>. Every unit plays
    /// the Secrets before it starts and a release starts them together: two <c>kube play --replace</c> of one Secret at
    /// once tear it down and create it over each other, and one fails with "secret name in use".
    /// </summary>
    public static string PlaySecret(string file, string secretPath) =>
        $"{Flock} {SecretLockPath(secretPath)} {Podman} kube play --replace {file}";

    public static PodmanStack Render(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        string directory,
        string secretPath,
        bool force = false
    )
    {
        var env = PodmanEnvironment(file, environment);
        var cluster = KubernetesTarget.Render(file, environment, exports, tag, eks: null, force);
        var stack = file.StackName(environment);
        var r = new Units(file, environment, stack, directory, secretPath);

        var files = new List<(string Path, string Content)> { ($"{stack}.network", r.Network()) };
        files.AddRange(OneShots(cluster, stack, r));
        var backing = BackingUnits(cluster, stack, r, files);

        var migrator = ComposeTarget.Migrator(file, exports);
        var units = new List<string>(backing);
        var serviceUnits = new List<string>();
        var migratorUnit = $"{stack}-{migrator}";
        var tailnet = KubernetesTailnet.Exposed(env);
        foreach (var d in cluster.Services.OfType<V1Deployment>())
        {
            var service = d.Metadata.Name;
            var name = $"{stack}-{service}";
            var exposed = tailnet && ComposeTarget.Hub(exports[service]);
            files.Add(($"{name}.yaml", r.Yaml(Local(d, stack))));
            var isMigrator = string.Equals(service, migrator, StringComparison.Ordinal);
            files.Add(
                (
                    $"{name}.kube",
                    r.Kube(
                        name,
                        after: isMigrator ? backing : [.. backing, migratorUnit],
                        migrate: isMigrator
                            ? new Migration(
                                env.Database is { InCluster: true },
                                file.Services[migrator].Egress is not null
                            )
                            : null,
                        port: !tailnet && ComposeTarget.Hub(exports[service])
                            ? file.Services[service].Port
                            : null,
                        proxySecret: exposed ? ProxySecretPath(secretPath) : null
                    )
                )
            );
            units.Add(name);
            serviceUnits.Add(name);
        }
        if (tailnet)
            units.Add(TailnetFiles(file, environment, exports, cluster, r, files));
        return new PodmanStack(
            stack,
            files,
            Secrets(cluster),
            units,
            serviceUnits,
            secretPath,
            cluster.ProxySecretName
        );
    }

    /// <summary>
    /// Adds a tailnet exposure's pod and unit beside the hub's, and returns the unit. Requires= on the hub's unit stops
    /// and restarts it with the hub, so the edge reads the proxy secret the hub was started with.
    /// </summary>
    private static string TailnetFiles(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        KubernetesManifests cluster,
        Units r,
        List<(string Path, string Content)> files
    )
    {
        var env = file.Environment(environment);
        var stack = file.StackName(environment);
        var name = $"{stack}-{KubernetesTailnet.EdgeName}";
        // KubernetesTarget.Render has refused a tailnet exposure without exactly one hub.
        var hubService = file.Services.Keys.Single(s => ComposeTarget.Hub(exports[s]));
        var pod = Tailnet(
            file.App,
            stack,
            hubService,
            cluster.SecretName,
            cluster.ProxySecretName.NotNull(),
            KubernetesTailnet.Hostname(file, environment, env)
        );
        files.Add(($"{name}.yaml", r.Yaml(pod)));
        files.Add(
            (
                $"{name}.kube",
                r.Kube(
                    name,
                    after: [$"{stack}-{hubService}"],
                    migrate: null,
                    proxySecret: ProxySecretPath(r.SecretPath)
                )
            )
        );
        return name;
    }

    /// <summary>The environment, refused when it does not run under podman.</summary>
    private static DeployEnvironment PodmanEnvironment(DeployFile file, string environment)
    {
        var env = file.Environment(environment);
        if (!string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Environment '{environment}' does not run under podman. Give it \"runtime\": \"podman\"."
            );
        return env;
    }

    /// <summary>Adds the files of Postgres and RabbitMQ when the environment runs them, and returns their units.</summary>
    private static List<string> BackingUnits(
        KubernetesManifests cluster,
        string stack,
        Units r,
        List<(string Path, string Content)> files
    )
    {
        var backing = new List<string>();
        foreach (var o in cluster.Backing.Where(o => o is V1Deployment or V1StatefulSet))
        {
            var (name, objects) = Backing(o, stack);
            files.Add(($"{name}.yaml", r.Yaml([.. objects])));
            files.Add(($"{name}.kube", r.Kube(name, after: [], migrate: null)));
            backing.Add(name);
        }
        return backing;
    }

    /// <summary>
    /// A tailnet exposure's pod, beside the hub's: the edge, which sets the proxy secret on every request and passes it
    /// to <paramref name="hub"/> on the app's network, and Tailscale, which serves HTTPS on the tailnet to the edge.
    /// Before it, the ConfigMap with the edge's Caddyfile and Tailscale's serve configuration, and the claim Tailscale
    /// keeps its node key on.
    /// <para>
    /// The pod has a network namespace of its own and nothing in it listens on TCP: the edge listens on a Unix socket
    /// in a directory only this pod mounts, which serve dials. In userspace networking tailscaled hands any tailnet
    /// connection that serve does not take to 127.0.0.1 on the same port, so a TCP listener here, or the hub sharing
    /// this namespace, would be reachable from the tailnet without serve's identity headers. Tailscale is held to no
    /// egress list, as it never was, and runs as a user of its own.
    /// </para>
    /// </summary>
    private static IKubernetesObject<V1ObjectMeta>[] Tailnet(
        string app,
        string stack,
        string hub,
        string appSecret,
        string proxySecret,
        string hostname
    )
    {
        var name = $"{stack}-{KubernetesTailnet.EdgeName}";
        var config = TailnetConfig(name, hub);
        var claim = TailscaleClaim(stack);
        var edge = KubernetesTarget.EdgeContainer(proxySecret);
        edge.Ports = null;
        edge.ReadinessProbe = null;
        var tailscale = KubernetesTarget.TailscaleSidecar(appSecret, hostname);
        tailscale.SecurityContext.RunAsUser = KubernetesTailnet.TailscaleUser;
        tailscale.SecurityContext.RunAsGroup = KubernetesTailnet.TailscaleUser;
        tailscale.SecurityContext.RunAsNonRoot = true;
        var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
        TailnetContainer(edge, "edge-tmp", annotations);
        TailnetContainer(tailscale, "tailscale-tmp", annotations);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesTarget.NameLabel] = KubernetesTailnet.EdgeName,
            [KubernetesTarget.AppLabel] = app,
        };
        var deployment = new V1Deployment
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new V1ObjectMeta { Name = name, Labels = labels },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector { MatchLabels = labels },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels, Annotations = annotations },
                    Spec = new V1PodSpec
                    {
                        AutomountServiceAccountToken = false,
                        EnableServiceLinks = false,
                        Containers = [edge, tailscale],
                        Volumes = TailnetVolumes(name, claim.Metadata.Name),
                    },
                },
            },
        };
        return [config, claim, deployment];
    }

    /// <summary>
    /// Readies a container of the tailnet pod: its <c>/tmp</c> is <paramref name="scratch"/>, it mounts the directory
    /// the edge's socket is in, its image is named in full, and it runs under the runtime's seccomp profile.
    /// </summary>
    private static void TailnetContainer(
        V1Container container,
        string scratch,
        Dictionary<string, string> annotations
    )
    {
        foreach (var mount in container.VolumeMounts)
            if (string.Equals(mount.Name, KubernetesTarget.ScratchVolume, StringComparison.Ordinal))
                mount.Name = scratch;
        container.VolumeMounts.Add(
            new V1VolumeMount
            {
                Name = "edge-socket",
                MountPath = KubernetesTailnet.EdgeSocketDirectory,
            }
        );
        container.Image = FullImage(container.Image);
        annotations[$"container.seccomp.security.alpha.kubernetes.io/{container.Name}"] =
            "runtime/default";
    }

    /// <summary>
    /// The tailnet pod's volumes: a <c>/tmp</c> for each container, the directory the edge's socket is in, the
    /// ConfigMap <paramref name="name"/>, and the claim Tailscale keeps its node key on.
    /// </summary>
    private static List<V1Volume> TailnetVolumes(string name, string claim) =>
        [
            new V1Volume { Name = "edge-tmp", EmptyDir = new V1EmptyDirVolumeSource() },
            new V1Volume { Name = "tailscale-tmp", EmptyDir = new V1EmptyDirVolumeSource() },
            new V1Volume { Name = "edge-socket", EmptyDir = new V1EmptyDirVolumeSource() },
            KubernetesTarget.EdgeConfigVolume(name),
            new V1Volume
            {
                Name = "tailscale-config",
                ConfigMap = new V1ConfigMapVolumeSource { Name = name },
            },
            new V1Volume
            {
                Name = "tailscale-state",
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = claim,
                },
            },
        ];

    /// <summary>
    /// The edge's Caddyfile, listening on its socket and passing to the hub by its name on the app's network, and
    /// Tailscale's serve configuration.
    /// </summary>
    private static V1ConfigMap TailnetConfig(string configName, string hub) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new V1ObjectMeta { Name = configName },
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesTarget.EdgeConfigFile] = KubernetesTailnet.Caddyfile(
                    bind: KubernetesTailnet.EdgeSocketBind,
                    upstream: string.Create(
                        CultureInfo.InvariantCulture,
                        $"{hub}:{KubernetesTarget.HttpPort}"
                    )
                ),
                [KubernetesTarget.TailscaleServeFile] = KubernetesTailnet.ServeConfig(),
            },
        };

    /// <summary>The volume Tailscale keeps its node key on, so a restarted pod is the same machine on the tailnet.</summary>
    private static V1PersistentVolumeClaim TailscaleClaim(string stack) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "PersistentVolumeClaim",
            Metadata = new V1ObjectMeta { Name = $"{stack}-tailscale" },
            Spec = new V1PersistentVolumeClaimSpec
            {
                AccessModes = ["ReadWriteOnce"],
                Resources = new V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                    {
                        ["storage"] = new ResourceQuantity("16Mi"),
                    },
                },
            },
        };

    /// <summary>
    /// The proxy secret as a Secret document, which the hub's unit plays before its pod. A deploy writes a new one
    /// every time, to <see cref="ProxySecretPath"/>.
    /// </summary>
    public static string ProxySecretYaml(PodmanStack stack, string secret)
    {
        var name = stack.ProxySecretName.NotNull();
        var filled = Secret(name, [KubernetesTailnet.ProxySecretKey]);
        filled.StringData[KubernetesTailnet.ProxySecretKey] = secret;
        return "# Written by trax deploy: the tailnet proxy's secret, new on every deploy. Keep it mode 0600 and never "
            + "commit it.\n---\n"
            + Document(filled);
    }

    /// <summary>The Secrets with their keys and no values; <see cref="SecretYaml"/> fills them.</summary>
    private static List<V1Secret> Secrets(KubernetesManifests cluster)
    {
        var secrets = new List<V1Secret>();
        if (cluster.SecretKeys.Count > 0)
            secrets.Add(Secret(cluster.SecretName, cluster.SecretKeys));
        if (cluster.DatabaseSecret is { } db)
            secrets.Add(Secret(db.Name, [db.Key]));
        return secrets;
    }

    private static V1Secret Secret(string name, IEnumerable<string> keys) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "Secret",
            Type = "Opaque",
            Metadata = new V1ObjectMeta { Name = name },
            StringData = keys.ToDictionary(k => k, _ => "", StringComparer.Ordinal),
        };

    /// <summary>
    /// The Secrets with their values from <paramref name="values"/>, as one YAML file the units play before their pods.
    /// Refuses, naming them, when any key has no value.
    /// </summary>
    public static string SecretYaml(
        PodmanStack stack,
        IReadOnlyDictionary<string, string> values,
        string source
    )
    {
        var missing = stack
            .SecretKeys.Where(k => !values.ContainsKey(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"{source} has no value for {string.Join(", ", missing)}. Add a NAME=value line for each; nothing "
                    + "was built or written."
            );
        var text = new StringBuilder(
            $"# Written by trax deploy from {source}. It holds secret values: keep it mode 0600 and never commit it.\n"
        );
        foreach (var secret in stack.Secrets)
        {
            var filled = Secret(secret.Metadata.Name, secret.StringData.Keys);
            foreach (var key in secret.StringData.Keys)
                filled.StringData[key] = values[key];
            text.Append("---\n").Append(Document(filled));
        }
        return text.ToString();
    }

    /// <summary>
    /// One YAML document ending in a newline, so the <c>---</c> after it stands on a line of its own; the serializer
    /// ends the last line without one.
    /// </summary>
    private static string Document(object o) => KubernetesYaml.Serialize(o).TrimEnd('\n') + "\n";

    /// <summary>
    /// A cluster object made a podman one: named after the stack (podman's pod names are global), no namespace, no
    /// <c>hostUsers</c>, and its images named in full so podman never asks which registry a short name means.
    /// </summary>
    private static T Local<T>(T o, string stack)
        where T : IKubernetesObject<V1ObjectMeta>
    {
        o.Metadata.Name = $"{stack}-{o.Metadata.Name}";
        o.Metadata.NamespaceProperty = null;
        var pod = o switch
        {
            V1Deployment d => d.Spec.Template.Spec,
            V1Pod p => p.Spec,
            _ => null,
        };
        if (pod is not null)
        {
            pod.HostUsers = null;
            HoldEgress(o, pod);
            foreach (var c in pod.Containers.Concat(pod.InitContainers ?? []))
            {
                c.Image = FullImage(c.Image);
                // A preStop gives a cluster's Service time to stop routing; a unit's restart stops the pod outright.
                c.Lifecycle = null;
                // podman runs an httpGet probe as curl inside the container, which a Trax image does not have, so the
                // probe always fails and a liveness probe restarts the container every few dozen seconds. A restarted
                // hub runs its startup recovery, which fails the runs still live on it, and a drain could never hold
                // one. Nothing routes on readiness under podman either; the deploy checks /trax/health from outside.
                c.LivenessProbe = null;
                c.ReadinessProbe = null;
            }
        }
        return o;
    }

    /// <summary>
    /// The annotation that makes <c>kube play</c> run a pod's init containers on every start of the pod, not only
    /// the first (podman 6.2; the default, <c>once</c>, removes them after their first run).
    /// </summary>
    public const string InitContainerTypeAnnotation = "io.podman.annotations.init.container.type";

    /// <summary>
    /// The egress as podman runs it. <c>kube play</c> has no native sidecar: it waits for every init container to
    /// exit, so one that keeps running would hold the pod from starting (checked on podman 6.2). The sidecar becomes
    /// an ordinary container that runs <c>run</c>: it applies the rules itself, as root with NET_ADMIN, then gives up
    /// root and every capability and serves. The rules init container stays, and runs on every start of the pod,
    /// so the rules are in place before any container does. Both are needed: <c>podman pod restart</c> makes the
    /// pod's network namespace afresh and does not run init containers again, and the sidecar putting the rules back
    /// as it starts is what holds the namespace then.
    /// </summary>
    private static void HoldEgress<T>(T o, V1PodSpec pod)
        where T : IKubernetesObject<V1ObjectMeta>
    {
        var sidecars =
            pod.InitContainers?.Where(c =>
                    string.Equals(c.RestartPolicy, "Always", StringComparison.Ordinal)
                )
                .ToList()
            ?? [];
        if (pod.InitContainers is not { Count: > 0 })
            return;
        // kube play under --userns=nomap cannot start a container in a pod that shares its PID namespace (crun 1.31:
        // "open `.`: Permission denied"), so here the sidecar does not see the app's processes.
        pod.ShareProcessNamespace = null;
        foreach (var sidecar in sidecars)
        {
            pod.InitContainers.Remove(sidecar);
            sidecar.RestartPolicy = null;
            sidecar.StartupProbe = null;
            sidecar.Args = [DeployEgress.RunCommand];
            sidecar.SecurityContext.RunAsUser = 0;
            sidecar.SecurityContext.RunAsGroup = 0;
            sidecar.SecurityContext.RunAsNonRoot = false;
            sidecar.SecurityContext.Capabilities.Add = ["NET_ADMIN", "SETGID", "SETUID"];
            pod.Containers.Add(sidecar);
        }
        var meta = o switch
        {
            V1Deployment d => d.Spec.Template.Metadata,
            V1Pod p => p.Metadata,
            _ => null,
        };
        if (meta is not null && pod.InitContainers.Count > 0)
        {
            meta.Annotations ??= new Dictionary<string, string>(StringComparer.Ordinal);
            meta.Annotations[InitContainerTypeAnnotation] = "always";
        }
    }

    /// <summary>
    /// The image podman stores a name under: <c>localhost/</c> for the ones trax built, <c>docker.io/library/</c>
    /// for the official images.
    /// </summary>
    internal static string FullImage(string image) =>
        image.Contains('/', StringComparison.Ordinal) ? image
        : image.StartsWith("postgres:", StringComparison.Ordinal)
        || image.StartsWith("rabbitmq:", StringComparison.Ordinal)
            ? $"docker.io/library/{image}"
        : $"localhost/{image}";

    /// <summary>
    /// The migrate, drain and resume pods, each run once. A cluster runs each as a Job; here each is a Pod that never
    /// restarts, since <c>kube play</c> runs a Job only from podman 5 and a Pod on every version. The pod is named
    /// <c>&lt;stack&gt;-&lt;kind&gt;-pod</c>, so its container is <c>&lt;stack&gt;-&lt;kind&gt;-pod-&lt;kind&gt;</c>,
    /// the names a Job's pod gets and the units wait on.
    /// </summary>
    private static IEnumerable<(string Path, string Content)> OneShots(
        KubernetesManifests cluster,
        string stack,
        Units r
    ) =>
        new[]
        {
            (KubernetesTarget.MigrateName, cluster.Migrate),
            (KubernetesTarget.DrainName, cluster.Drain),
            (KubernetesTarget.ResumeName, cluster.Resume),
        }.Select(o => ($"{stack}-{o.Item1}.yaml", r.Yaml(OneShot(o.Item2, o.Item1, stack))));

    private static V1Pod OneShot(V1Job job, string kind, string stack)
    {
        var template = job.Spec.Template;
        template.Spec.RestartPolicy = "Never";
        return Local(
            new V1Pod
            {
                ApiVersion = "v1",
                Kind = "Pod",
                Metadata = new V1ObjectMeta
                {
                    Name = $"{kind}-pod",
                    Labels = template.Metadata.Labels,
                    Annotations = template.Metadata.Annotations,
                },
                Spec = template.Spec,
            },
            stack
        );
    }

    /// <summary>
    /// Postgres or RabbitMQ as a Deployment of one. Postgres's StatefulSet becomes a Deployment with a claim of its
    /// own, which podman keeps as a named volume (<c>&lt;app&gt;-&lt;env&gt;-data</c>) across restarts.
    /// </summary>
    private static (string Name, List<IKubernetesObject<V1ObjectMeta>> Objects) Backing(
        IKubernetesObject<V1ObjectMeta> o,
        string stack
    )
    {
        if (o is V1Deployment d)
        {
            var local = Local(d, stack);
            return (local.Metadata.Name, [local]);
        }
        var set = (V1StatefulSet)o;
        var claim = set.Spec.VolumeClaimTemplates.Single();
        claim.ApiVersion = "v1";
        claim.Kind = "PersistentVolumeClaim";
        claim.Metadata.Name = $"{stack}-{claim.Metadata.Name}";
        var template = set.Spec.Template;
        template.Spec.Volumes =
        [
            new V1Volume
            {
                Name = "data",
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = claim.Metadata.Name,
                },
            },
        ];
        var deployment = Local(
            new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = set.Metadata,
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Strategy = new V1DeploymentStrategy { Type = "Recreate" },
                    Selector = set.Spec.Selector,
                    Template = template,
                },
            },
            stack
        );
        return (deployment.Metadata.Name, [claim, deployment]);
    }

    /// <summary>
    /// What the migrator's unit runs before its pod: whether it first waits for the database on this host, and
    /// whether the migrate pod has an egress sidecar, which keeps running after the migration exits and is stopped.
    /// </summary>
    private sealed record Migration(bool WaitForDatabase, bool StopEgress);

    private sealed class Units(
        DeployFile file,
        string environment,
        string stack,
        string directory,
        string secretPath
    )
    {
        public string SecretPath => secretPath;

        private string Header =>
            $"# Written by `trax deploy --env {environment}` from trax.deploy.json. Install it with the other units of "
            + $"{stack} under ~/.config/containers/systemd/.\n";

        public string Yaml(params IKubernetesObject<V1ObjectMeta>[] objects) =>
            $"# Written by `trax deploy --env {environment}` from trax.deploy.json, for podman kube play.\n"
            + string.Join("---\n", objects.Select(Document));

        public string Network() => Header + "\n[Network]\n" + $"NetworkName={stack}\n";

        public string Kube(
            string name,
            IReadOnlyList<string> after,
            Migration? migrate,
            int? port = null,
            string? proxySecret = null
        )
        {
            var u = new StringBuilder(Header);
            u.Append("\n[Unit]\n")
                .Append($"Description={file.App} {name[(stack.Length + 1)..]} ({environment})\n");
            foreach (var other in after)
                u.Append($"Requires={other}.service\nAfter={other}.service\n");
            u.Append("\n[Kube]\n")
                .Append($"Yaml={Path.Combine(directory, $"{name}.yaml")}\n")
                .Append($"Network={stack}.network\n")
                .Append("UserNS=nomap\n")
                .Append("ExitCodePropagation=any\n");
            if (port is { } p)
                u.Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PublishPort=127.0.0.1:{p}:{KubernetesTarget.HttpPort}\n"
                    )
                );
            u.Append("\n[Service]\n")
                .Append($"ExecStartPre={PlaySecret(secretPath, secretPath)}\n");
            if (proxySecret is not null)
                u.Append($"ExecStartPre={PlaySecret(proxySecret, secretPath)}\n");
            if (migrate is not null)
                Migrations(u, migrate);
            u.Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"TimeoutStartSec={StartTimeoutSeconds}\n"
                    )
                )
                .Append("Restart=on-failure\nRestartSec=10\n")
                .Append("\n[Install]\nWantedBy=default.target\n");
            return u.ToString();
        }

        /// <summary>
        /// Waits for this host's Postgres, plays the migrate pod and fails the unit unless it exits 0, printing the end
        /// of its log. <c>$$</c> is systemd's escape for the shell's <c>$</c>.
        /// </summary>
        private void Migrations(StringBuilder u, Migration migrate)
        {
            var container =
                $"{stack}-{KubernetesTarget.MigrateName}-pod-{KubernetesTarget.MigrateName}";
            if (migrate.WaitForDatabase)
            {
                var database =
                    $"{stack}-{KubernetesTarget.Database}-pod-{KubernetesTarget.Database}";
                u.Append("ExecStartPre=/bin/sh -c 'for i in $$(seq 120); do ")
                    .Append($"{Podman} exec {database} pg_isready -q -U {file.App} && exit 0; ")
                    .Append(
                        "sleep 1; done; echo \"the database did not become ready\" >&2; exit 1'\n"
                    );
            }
            u.Append($"ExecStartPre={Podman} kube play --replace --userns=nomap --network {stack} ")
                .Append(Path.Combine(directory, $"{stack}-{KubernetesTarget.MigrateName}.yaml"))
                .Append('\n')
                .Append($"ExecStartPre=/bin/sh -c 'code=$$({Podman} wait {container}); ")
                .Append("[ \"$$code\" = 0 ] || ")
                .Append($"{{ {Podman} logs --tail 50 {container} >&2; ")
                .Append(
                    "echo \"the migration failed (exit $$code), so no service was started\" >&2; "
                )
                .Append("exit 1; }'\n");
            if (migrate.StopEgress)
                u.Append(
                    $"ExecStartPre=-{Podman} pod stop --ignore -t 10 {stack}-{KubernetesTarget.MigrateName}-pod\n"
                );
        }
    }
}
