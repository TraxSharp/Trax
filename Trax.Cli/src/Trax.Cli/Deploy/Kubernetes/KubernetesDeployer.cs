using System.Globalization;
using k8s.Models;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Puts an environment on a Kubernetes cluster: checks the namespace is this app's and its Secret has every key,
/// builds the images, brings up the in-cluster Postgres and RabbitMQ, runs the migrate Job, and only after it succeeds
/// applies the services and waits for each to roll out. A service that does not roll out puts every service back on
/// the version it ran before the deploy, as ECS's circuit breaker does. It never creates a cluster.
/// </summary>
internal sealed class KubernetesDeployer(
    IKubernetesDeployClient cluster,
    IContainerImagePublisher images,
    Action<string> say,
    Func<string>? mintProxySecret = null
)
{
    private readonly Func<string> _mintProxySecret =
        mintProxySecret ?? KubernetesTailnet.MintSecret;

    public static readonly TimeSpan RolloutTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MigrateTimeout = TimeSpan.FromMinutes(30);

    public async Task DeployAsync(
        DeployFile file,
        string filePath,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        CancellationToken ct,
        EksTarget? eks = null
    )
    {
        var env = file.Environment(environment);
        var manifests = KubernetesTarget.Render(file, environment, exports, tag, eks);
        say(
            $"Deploying {file.App} to namespace {manifests.NamespaceName} in context {cluster.Context}."
        );
        await PrepareNamespaceAsync(file, environment, manifests, ct).ConfigureAwait(false);
        // On EKS the images were pushed to ECR before the foundation was deployed.
        if (eks is null)
            await PublishImagesAsync(file, filePath, env, tag).ConfigureAwait(false);

        // The policies go first, so the in-cluster database and broker are never up without them.
        await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Policies, ct)
            .ConfigureAwait(false);
        await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Backing, ct)
            .ConfigureAwait(false);
        await MigrateAsync(environment, manifests, ct).ConfigureAwait(false);
        await RotateProxySecretAsync(file, manifests, ct).ConfigureAwait(false);

        say($"Rolling the services onto {tag}...");
        var before = await DeploymentsBeforeAsync(manifests, ct).ConfigureAwait(false);
        try
        {
            await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Services, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException failed) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"{failed.Message} {await RollBackAsync(manifests.NamespaceName, before, ct).ConfigureAwait(false)}",
                failed
            );
        }
    }

    /// <summary>
    /// On a tailnet exposure, writes a new proxy secret to its Secret, which the hub and the edge both restart onto
    /// in this rollout. <see cref="PrepareNamespaceAsync"/> has refused a Secret of that name trax did not write.
    /// </summary>
    private async Task RotateProxySecretAsync(
        DeployFile file,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        if (manifests.ProxySecretName is not { } name)
            return;
        var ns = manifests.NamespaceName;
        say($"Rotating the tailnet proxy's secret ({name})...");
        await cluster
            .ApplyAsync(
                new V1Secret
                {
                    ApiVersion = "v1",
                    Kind = "Secret",
                    Type = "Opaque",
                    Metadata = new V1ObjectMeta
                    {
                        Name = name,
                        NamespaceProperty = ns,
                        Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [KubernetesTarget.ManagedByLabel] = KubernetesTarget.ManagedBy,
                            [KubernetesTarget.AppLabel] = file.App,
                        },
                    },
                    StringData = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [KubernetesTailnet.ProxySecretKey] = _mintProxySecret(),
                    },
                },
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The Deployment each rendered service had before this deploy changes it, by name, or null for one this deploy
    /// creates.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, V1Deployment?>> DeploymentsBeforeAsync(
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var before = new Dictionary<string, V1Deployment?>(StringComparer.Ordinal);
        foreach (var deployment in manifests.Services.OfType<V1Deployment>())
            before[deployment.Metadata.Name] = await cluster
                .ReadDeploymentAsync(manifests.NamespaceName, deployment.Metadata.Name, ct)
                .ConfigureAwait(false);
        return before;
    }

    /// <summary>
    /// Puts every service back as it was before this deploy: a Deployment that existed gets its earlier spec (its pod
    /// template, copies and strategy) applied again and is waited on; one this deploy created is deleted. A first deploy,
    /// with nothing to go back to, is left as it is. The migration is not undone: a migration must leave the version
    /// before it working, since that version serves while it runs. Returns the sentence the failure ends with.
    /// </summary>
    private async Task<string> RollBackAsync(
        string ns,
        IReadOnlyDictionary<string, V1Deployment?> before,
        CancellationToken ct
    )
    {
        if (before.Values.All(d => d is null))
            return "Nothing was rolled back: no service ran before this deploy. Fix it and deploy again.";
        say("Rolling the services back to the version before this deploy...");
        try
        {
            foreach (var (name, previous) in before)
                if (previous is null)
                    await cluster
                        .DeleteAsync(
                            ns,
                            new KubernetesObjectState("Deployment", name, App: null),
                            ct
                        )
                        .ConfigureAwait(false);
                else
                    await cluster.ApplyAsync(Restored(ns, previous), ct).ConfigureAwait(false);
            foreach (var (name, previous) in before)
                if (previous is not null)
                    await cluster
                        .WaitForRolloutAsync(ns, "Deployment", name, RolloutTimeout, ct)
                        .ConfigureAwait(false);
        }
        catch (InvalidOperationException rollback)
        {
            return $"Rolling back failed too, so the services are in between versions: {rollback.Message}";
        }
        return "Every service was rolled back to the version it ran before this deploy; the migration stays.";
    }

    /// <summary>
    /// <paramref name="previous"/> as a body to apply: its name, labels and spec, without the status and the
    /// server-set metadata (uid, resource version, managed fields) that an apply refuses.
    /// </summary>
    internal static V1Deployment Restored(string ns, V1Deployment previous) =>
        new()
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new V1ObjectMeta
            {
                Name = previous.Metadata.Name,
                NamespaceProperty = ns,
                Labels = previous.Metadata.Labels,
            },
            Spec = previous.Spec,
        };

    /// <summary>
    /// Makes the namespace this app's to apply to: refuses it or any object in it another app deployed, and a Secret
    /// without every key the app needs, then creates the namespace when it is missing. A missing namespace has no
    /// Secret, so it is refused before it is made unless the app reads none. Nothing is applied before this passes.
    /// </summary>
    private async Task PrepareNamespaceAsync(
        DeployFile file,
        string environment,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var ns = manifests.NamespaceName;
        var labels = await cluster.NamespaceLabelsAsync(ns, ct).ConfigureAwait(false);
        var exists = labels is not null;
        if (labels is not null)
            CheckOwner(
                file,
                $"Namespace {ns}",
                labels.GetValueOrDefault(KubernetesTarget.AppLabel),
                ownerless: true
            );

        await CheckSecretAsync(
                ns,
                manifests.SecretName,
                manifests.SecretKeys,
                environment,
                exists,
                ct
            )
            .ConfigureAwait(false);
        if (manifests.DatabaseSecret is { } database)
            await CheckSecretAsync(ns, database.Name, [database.Key], environment, exists, ct)
                .ConfigureAwait(false);

        if (!exists)
        {
            say($"Creating namespace {ns}...");
            await cluster.CreateNamespaceAsync(manifests.Namespace, ct).ConfigureAwait(false);
        }

        await CheckProxySecretOwnerAsync(file, manifests, ct).ConfigureAwait(false);

        var applying = manifests.All.Skip(1).Select(o => (o.Kind, o.Metadata.Name)).ToHashSet();
        foreach (var existing in await cluster.ListAsync(ns, ct).ConfigureAwait(false))
            if (existing.App is not null || applying.Contains((existing.Kind, existing.Name)))
                CheckOwner(
                    file,
                    $"{existing.Kind} {existing.Name} in namespace {ns}",
                    existing.App,
                    ownerless: false
                );
    }

    /// <summary>Refuses a Secret named like the tailnet proxy's that this app's deploy did not write.</summary>
    private async Task CheckProxySecretOwnerAsync(
        DeployFile file,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var ns = manifests.NamespaceName;
        if (
            manifests.ProxySecretName is { } proxySecret
            && await cluster.SecretLabelsAsync(ns, proxySecret, ct).ConfigureAwait(false)
                is { } held
        )
            CheckOwner(
                file,
                $"Secret {proxySecret} in namespace {ns}",
                held.GetValueOrDefault(KubernetesTarget.AppLabel),
                ownerless: false
            );
    }

    private async Task PublishImagesAsync(
        DeployFile file,
        string filePath,
        DeployEnvironment env,
        string tag
    )
    {
        say(
            env.Registry is null
                ? $"Building images tagged {tag} into the local container runtime..."
                : $"Pushing images tagged {tag} to {env.Registry}..."
        );
        foreach (var (name, service) in file.Services)
        {
            say($"  {name} -> {KubernetesTarget.Image(file, env, name, tag)}");
            await images
                .PublishAsync(
                    DeployFile.ProjectPath(filePath, service),
                    env.Registry,
                    ComposeTarget.ImageName(file, name),
                    tag
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>Applies every object, then waits for each Deployment and StatefulSet among them to roll out.</summary>
    private async Task ApplyAndWaitAsync(
        string ns,
        IReadOnlyList<k8s.IKubernetesObject<V1ObjectMeta>> objects,
        CancellationToken ct
    )
    {
        foreach (var o in objects)
            await cluster.ApplyAsync(o, ct).ConfigureAwait(false);
        foreach (var o in objects.Where(Rolls))
        {
            say($"Waiting for {o.Metadata.Name}...");
            await cluster
                .WaitForRolloutAsync(ns, o.Kind, o.Metadata.Name, RolloutTimeout, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Runs the migrate Job to the end, and refuses to go on when it failed.</summary>
    private async Task MigrateAsync(
        string environment,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var ns = manifests.NamespaceName;
        var job = manifests.Migrate.Metadata.Name;
        say($"Migrating the database (job {job})...");
        await cluster.CreateJobAsync(manifests.Migrate, ct).ConfigureAwait(false);
        var migration = await cluster
            .WaitForJobAsync(ns, job, MigrateTimeout, ct)
            .ConfigureAwait(false);
        if (!migration.Succeeded)
            throw new InvalidOperationException(
                $"The migration failed{(migration.Reason is null ? "" : $" ({migration.Reason})")}, so no service "
                    + $"was moved to the new version. The end of its log:\n{migration.LogTail}\nAll of it: "
                    + $"'trax deploy logs migrate --env {environment}', or 'kubectl logs -n {ns} job/{job}'."
            );
    }

    /// <summary>
    /// Deletes the app's Deployments, StatefulSets, Jobs, Ingresses, Services, ConfigMaps and network policies in the
    /// namespace, and the tailnet proxy's Secret, which trax wrote. The in-cluster database's volume only with
    /// <paramref name="deleteData"/>. The namespace and the app's Secret stay: trax did not write that Secret, and the
    /// namespace may hold other things.
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
        var labels = await cluster.NamespaceLabelsAsync(ns, ct).ConfigureAwait(false);
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
        var mine = (await cluster.ListAsync(ns, ct).ConfigureAwait(false))
            .Where(o => string.Equals(o.App, file.App, StringComparison.Ordinal))
            .ToList();
        string[] order =
        [
            "Deployment",
            "StatefulSet",
            "Job",
            "Ingress",
            "Service",
            "ConfigMap",
            "ServiceAccount",
            "SecurityGroupPolicy",
            "NetworkPolicy",
        ];
        foreach (var kind in order)
        foreach (
            var target in mine.Where(o => string.Equals(o.Kind, kind, StringComparison.Ordinal))
        )
        {
            say($"Deleting {kind} {target.Name}...");
            await cluster.DeleteAsync(ns, target, ct).ConfigureAwait(false);
        }
        await DeleteProxySecretAsync(file, environment, ns, ct).ConfigureAwait(false);
        var claims = mine.Where(o =>
                string.Equals(o.Kind, "PersistentVolumeClaim", StringComparison.Ordinal)
            )
            .ToList();
        if (deleteData)
            foreach (var claim in claims)
            {
                say($"Deleting PersistentVolumeClaim {claim.Name} and the database in it...");
                await cluster.DeleteAsync(ns, claim, ct).ConfigureAwait(false);
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

    /// <summary>Deletes the tailnet proxy's Secret, when there is one and this app's deploy wrote it.</summary>
    private async Task DeleteProxySecretAsync(
        DeployFile file,
        string environment,
        string ns,
        CancellationToken ct
    )
    {
        var proxySecret = KubernetesTailnet.ProxySecretName(file, environment);
        if (
            await cluster.SecretLabelsAsync(ns, proxySecret, ct).ConfigureAwait(false) is { } held
            && string.Equals(
                held.GetValueOrDefault(KubernetesTarget.AppLabel),
                file.App,
                StringComparison.Ordinal
            )
        )
        {
            say($"Deleting Secret {proxySecret}...");
            await cluster
                .DeleteAsync(ns, new KubernetesObjectState("Secret", proxySecret, file.App), ct)
                .ConfigureAwait(false);
        }
    }

    private static bool Rolls(k8s.IKubernetesObject o) => o.Kind is "Deployment" or "StatefulSet";

    private async Task CheckSecretAsync(
        string ns,
        string secret,
        IReadOnlyList<string> keys,
        string environment,
        bool namespaceExists,
        CancellationToken ct
    )
    {
        if (keys.Count == 0)
            return;
        var held = await cluster.SecretKeysAsync(ns, secret, ct).ConfigureAwait(false);
        var missing = keys.Where(k => held?.Contains(k, StringComparer.Ordinal) is not true)
            .ToList();
        if (missing.Count == 0)
            return;
        throw new InvalidOperationException(
            (
                held is null
                    ? $"Namespace {ns} has no Secret {secret}, which must hold {string.Join(", ", missing)}."
                    : $"The Secret {secret} in namespace {ns} has no {string.Join(", ", missing)}."
            )
                + " Nothing was applied. Write every key it needs into a file, one KEY=value line each ("
                + string.Join(", ", keys)
                + "), and create it from that file, so no value is on a command line:\n  "
                + (namespaceExists ? "" : $"kubectl create namespace {ns}\n  ")
                + $"kubectl create secret generic {secret} -n {ns} --from-env-file=<file>"
                + (held is null ? "" : " --dry-run=client -o yaml | kubectl apply -f -")
                + $"\nthen delete the file and run 'trax deploy --env {environment}' again."
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
        if (
            string.Equals(owner, file.App, StringComparison.Ordinal) || (owner is null && ownerless)
        )
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
