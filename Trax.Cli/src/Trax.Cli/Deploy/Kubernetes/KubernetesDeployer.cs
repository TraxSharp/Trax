using System.Diagnostics;
using k8s.Models;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>Builds a service's image, and pushes it when the environment names a registry.</summary>
internal interface IContainerImagePublisher
{
    /// <summary>
    /// Publishes <paramref name="projectPath"/> as <paramref name="image"/>
    /// (<c>[&lt;registry&gt;/]&lt;repository&gt;:&lt;tag&gt;</c>): to the registry when the image names one, else to
    /// the local container runtime.
    /// </summary>
    void Publish(string projectPath, string? registry, string repository, string tag);
}

/// <summary>
/// Puts an environment on a Kubernetes cluster: checks the namespace is this app's and its Secret has every key,
/// builds the images, brings up the in-cluster Postgres and RabbitMQ, runs the migrate Job, and only after it succeeds
/// applies the services and waits for each to roll out. It never creates a cluster.
/// </summary>
internal sealed class KubernetesDeployer(
    IKubernetesDeployClient cluster,
    IContainerImagePublisher images,
    Action<string> say
)
{
    public static readonly TimeSpan RolloutTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MigrateTimeout = TimeSpan.FromMinutes(30);

    public async Task DeployAsync(
        DeployFile file,
        string filePath,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var manifests = KubernetesTarget.Render(file, environment, exports, tag);
        var ns = manifests.NamespaceName;
        say($"Deploying {file.App} to namespace {ns} in context {cluster.Context}.");

        var labels = await cluster.NamespaceLabelsAsync(ns, ct);
        if (labels is not null)
            CheckOwner(
                file,
                $"Namespace {ns}",
                labels.GetValueOrDefault(KubernetesTarget.AppLabel),
                ownerless: true
            );
        else
        {
            say($"Creating namespace {ns}...");
            await cluster.CreateNamespaceAsync(manifests.Namespace, ct);
        }

        // Nothing is applied until every secret the app needs has a value.
        await CheckSecretAsync(ns, manifests.SecretName, manifests.SecretKeys, environment, ct);
        if (manifests.DatabaseSecret is { } database)
            await CheckSecretAsync(ns, database.Name, [database.Key], environment, ct);

        var applying = manifests.All.Skip(1).Select(o => (o.Kind, o.Metadata.Name)).ToHashSet();
        foreach (var existing in await cluster.ListAsync(ns, ct))
            if (existing.App is not null || applying.Contains((existing.Kind, existing.Name)))
                CheckOwner(
                    file,
                    $"{existing.Kind} {existing.Name} in namespace {ns}",
                    existing.App,
                    ownerless: false
                );

        say(
            env.Registry is null
                ? $"Building images tagged {tag} into the local container runtime..."
                : $"Pushing images tagged {tag} to {env.Registry}..."
        );
        foreach (var (name, service) in file.Services)
        {
            say($"  {name} -> {KubernetesTarget.Image(file, env, name, tag)}");
            images.Publish(
                DeployFile.ProjectPath(filePath, service),
                env.Registry,
                ComposeTarget.ImageName(file, name),
                tag
            );
        }

        foreach (var backing in manifests.Backing)
            await cluster.ApplyAsync(backing, ct);
        foreach (var backing in manifests.Backing.Where(Rolls))
        {
            say($"Waiting for {backing.Metadata.Name}...");
            await cluster.WaitForRolloutAsync(
                ns,
                backing.Kind,
                backing.Metadata.Name,
                RolloutTimeout,
                ct
            );
        }

        var job = manifests.Migrate.Metadata.Name;
        say($"Migrating the database (job {job})...");
        await cluster.CreateJobAsync(manifests.Migrate, ct);
        var migration = await cluster.WaitForJobAsync(ns, job, MigrateTimeout, ct);
        if (!migration.Succeeded)
            throw new InvalidOperationException(
                $"The migration failed{(migration.Reason is null ? "" : $" ({migration.Reason})")}, so no service "
                    + $"was moved to the new version. The end of its log:\n{migration.LogTail}\nAll of it: "
                    + $"'trax deploy logs migrate --env {environment}', or 'kubectl logs -n {ns} job/{job}'."
            );

        say($"Rolling the services onto {tag}...");
        foreach (var service in manifests.Services)
            await cluster.ApplyAsync(service, ct);
        foreach (var deployment in manifests.Services.Where(Rolls))
        {
            say($"Waiting for {deployment.Metadata.Name}...");
            await cluster.WaitForRolloutAsync(
                ns,
                deployment.Kind,
                deployment.Metadata.Name,
                RolloutTimeout,
                ct
            );
        }
    }

    /// <summary>
    /// Deletes the app's Deployments, StatefulSets, Jobs and Services in the namespace. The in-cluster database's
    /// volume only with <paramref name="deleteData"/>. The namespace and the Secret stay: trax did not write the
    /// Secret, and the namespace may hold other things.
    /// </summary>
    public async Task DestroyAsync(
        DeployFile file,
        string environment,
        bool deleteData,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var ns = KubernetesTarget.NamespaceName(file, environment, env);
        var labels = await cluster.NamespaceLabelsAsync(ns, ct);
        if (labels is null)
        {
            say($"Namespace {ns} does not exist in context {cluster.Context}.");
            return;
        }
        CheckOwner(
            file,
            $"Namespace {ns}",
            labels.GetValueOrDefault(KubernetesTarget.AppLabel),
            ownerless: true
        );
        var mine = (await cluster.ListAsync(ns, ct)).Where(o => o.App == file.App).ToList();
        string[] order = ["Deployment", "StatefulSet", "Job", "Service"];
        foreach (var kind in order)
        foreach (var target in mine.Where(o => o.Kind == kind))
        {
            say($"Deleting {kind} {target.Name}...");
            await cluster.DeleteAsync(ns, target, ct);
        }
        var claims = mine.Where(o => o.Kind == "PersistentVolumeClaim").ToList();
        if (deleteData)
            foreach (var claim in claims)
            {
                say($"Deleting PersistentVolumeClaim {claim.Name} and the database in it...");
                await cluster.DeleteAsync(ns, claim, ct);
            }
        else if (claims.Count > 0)
            say(
                $"Kept the database's volume ({string.Join(", ", claims.Select(c => c.Name))}); the next deploy "
                    + $"of {environment} uses it again. 'trax deploy destroy --env {environment} --delete-data' "
                    + "deletes it."
            );
        say(
            $"The namespace {ns} and the Secret {KubernetesTarget.SecretName(file, environment)} were kept."
        );
    }

    private static bool Rolls(k8s.IKubernetesObject o) => o.Kind is "Deployment" or "StatefulSet";

    private async Task CheckSecretAsync(
        string ns,
        string secret,
        IReadOnlyList<string> keys,
        string environment,
        CancellationToken ct
    )
    {
        if (keys.Count == 0)
            return;
        var held = await cluster.SecretKeysAsync(ns, secret, ct);
        var missing = keys.Where(k => held?.Contains(k) is not true).ToList();
        if (missing.Count == 0)
            return;
        throw new InvalidOperationException(
            (
                held is null
                    ? $"Namespace {ns} has no Secret {secret}, which must hold {string.Join(", ", missing)}."
                    : $"The Secret {secret} in namespace {ns} has no {string.Join(", ", missing)}."
            )
                + " Nothing was applied. Create it before deploying, with every key it needs:\n  kubectl create "
                + $"secret generic {secret} -n {ns} "
                + string.Join(" ", keys.Select(k => $"--from-literal={k}=...")).TrimEnd()
                + (held is null ? "" : " --dry-run=client -o yaml | kubectl apply -f -")
                + $"\nthen run 'trax deploy --env {environment}' again."
        );
    }

    /// <summary>
    /// Refuses an object another app deployed: an app and an environment name can join to another app's namespace,
    /// and applying to it would move the other app's services onto this app's images. A namespace with no
    /// <c>trax/app</c> label (one made with kubectl for the Secret) is this app's to use; any other object must carry
    /// this app's label.
    /// </summary>
    private static void CheckOwner(DeployFile file, string what, string? owner, bool ownerless)
    {
        if (owner == file.App || (owner is null && ownerless))
            return;
        throw new InvalidOperationException(
            owner is null
                ? $"{what} has no {KubernetesTarget.AppLabel} label, so trax deploy did not create it. trax "
                    + "changes only the objects it created. Nothing was applied."
                : $"{what} belongs to app '{owner}', not '{file.App}'. Name another namespace for this "
                    + "environment. Nothing was applied."
        );
    }
}

/// <summary>
/// Builds a service's image with <c>dotnet publish -t:PublishContainer</c> for linux-x64: pushed to the registry when
/// one is named, with whatever login docker or podman holds for it, else loaded into the local container runtime.
/// </summary>
internal sealed class DotnetContainerPublisher : IContainerImagePublisher
{
    public void Publish(string projectPath, string? registry, string repository, string tag)
    {
        var (exit, stdout, stderr) = ChildProcess.Run(
            StartInfo(projectPath, registry, repository, tag)
        );
        if (exit != 0)
            throw new InvalidOperationException(
                $"Publishing {Path.GetFileName(projectPath)} as {repository}:{tag} failed (exit {exit}):\n{stdout}{stderr}"
            );
    }

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string? registry,
        string repository,
        string tag
    )
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        string[] args =
        [
            "publish",
            projectPath,
            "-c",
            "Release",
            "--os",
            "linux",
            "--arch",
            "x64",
            "-t:PublishContainer",
            $"-p:ContainerImageTags={tag}",
        ];
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        if (registry is null)
            psi.ArgumentList.Add($"-p:ContainerRepository={repository}");
        else
        {
            // ghcr.io/acme is the registry ghcr.io and the repository acme/<image>.
            var slash = registry.IndexOf('/');
            psi.ArgumentList.Add(
                $"-p:ContainerRegistry={(slash < 0 ? registry : registry[..slash])}"
            );
            psi.ArgumentList.Add(
                $"-p:ContainerRepository={(slash < 0 ? "" : registry[(slash + 1)..] + "/")}{repository}"
            );
        }
        return psi;
    }
}
