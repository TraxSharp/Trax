using System.Globalization;
using System.Text;
using k8s;
using k8s.Models;
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
/// it. No secret value is written here: the Secrets are filled from <c>.env</c> by the deploy, into a file outside
/// the project.
/// </summary>
internal static class PodmanTarget
{
    /// <summary>The podman binary the units call, where Quadlet itself finds it.</summary>
    public const string Podman = "/usr/bin/podman";

    /// <summary>How long a unit may take to start, the migration included: thirty minutes, as on a cluster.</summary>
    public const int StartTimeoutSeconds = 1800;

    /// <summary>The kinds <c>podman kube play</c> runs (podman 6.2's man page).</summary>
    public static readonly string[] PlayableKinds =
    [
        "Pod",
        "Deployment",
        "PersistentVolumeClaim",
        "ConfigMap",
        "Secret",
        "DaemonSet",
        "Job",
    ];

    /// <summary>Where a deploy writes the files a host installs: <c>.trax/deploy/podman/&lt;app&gt;-&lt;env&gt;/</c>.</summary>
    public static string DeployDirectory(string projectRoot, string stack) =>
        Path.Combine(projectRoot, ".trax", "deploy", "podman", stack);

    /// <summary>Where a deploy writes the Secrets with their values: <c>&lt;config&gt;/trax/&lt;app&gt;-&lt;env&gt;/secret.yaml</c>.</summary>
    public static string SecretPath(string configHome, string stack) =>
        Path.Combine(configHome, "trax", stack, "secret.yaml");

    public static PodmanStack Render(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        string directory,
        string secretPath
    )
    {
        var env = file.Environment(environment);
        if (!string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Environment '{environment}' does not run under podman. Give it \"runtime\": \"podman\"."
            );
        var cluster = KubernetesTarget.Render(file, environment, exports, tag);
        var stack = file.StackName(environment);
        var r = new Units(file, environment, stack, directory, secretPath);

        var files = new List<(string Path, string Content)>
        {
            ($"{stack}.network", r.Network()),
            (
                $"{stack}-{KubernetesTarget.MigrateName}.yaml",
                r.Yaml(Migrate(cluster.Migrate, stack))
            ),
        };
        var backing = new List<string>();
        foreach (var o in cluster.Backing.Where(o => o is V1Deployment or V1StatefulSet))
        {
            var (name, objects) = Backing(o, stack);
            files.Add(($"{name}.yaml", r.Yaml([.. objects])));
            files.Add(($"{name}.kube", r.Kube(name, after: [], migrate: null)));
            backing.Add(name);
        }

        var migrator = ComposeTarget.Migrator(file, exports);
        var units = new List<string>(backing);
        var migratorUnit = $"{stack}-{migrator}";
        foreach (var d in cluster.Services.OfType<V1Deployment>())
        {
            var service = d.Metadata.Name;
            var name = $"{stack}-{service}";
            files.Add(($"{name}.yaml", r.Yaml(Local(d, stack))));
            var isMigrator = string.Equals(service, migrator, StringComparison.Ordinal);
            files.Add(
                (
                    $"{name}.kube",
                    r.Kube(
                        name,
                        after: isMigrator ? backing : [.. backing, migratorUnit],
                        migrate: isMigrator
                            ? new Migration(env.Database is { InCluster: true })
                            : null,
                        port: ComposeTarget.Hub(exports[service])
                            ? file.Services[service].Port
                            : null
                    )
                )
            );
            units.Add(name);
        }
        return new PodmanStack(stack, files, Secrets(cluster), units);
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
            V1Job j => j.Spec.Template.Spec,
            _ => null,
        };
        if (pod is not null)
        {
            pod.HostUsers = null;
            foreach (var c in pod.Containers)
                c.Image = FullImage(c.Image);
        }
        return o;
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

    private static V1Job Migrate(V1Job job, string stack)
    {
        job.Metadata.Name = KubernetesTarget.MigrateName;
        job.Spec.TtlSecondsAfterFinished = null;
        return Local(job, stack);
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

    /// <summary>What the migrator's unit runs before its pod: whether it first waits for the database on this host.</summary>
    private sealed record Migration(bool WaitForDatabase);

    private sealed class Units(
        DeployFile file,
        string environment,
        string stack,
        string directory,
        string secretPath
    )
    {
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
            int? port = null
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
                .Append($"ExecStartPre={Podman} kube play --replace {secretPath}\n");
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
        }
    }
}
