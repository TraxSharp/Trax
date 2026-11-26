using System.Globalization;
using k8s.Models;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Puts an environment on a Kubernetes cluster: checks the namespace is this app's and its Secret has every key,
/// builds the images, applies the egress admission policy when a service names egress, brings up the in-cluster
/// Postgres and RabbitMQ, runs the migrate Job, and only after it succeeds drains the hosts (the drain Job, when
/// services ran before), applies the services and waits for each to roll out, then ends the drain (the resume Job). A
/// service that does not roll out puts every service back on the version it ran before the deploy, as ECS's circuit
/// breaker does, and the drain is ended after that too. It never creates a cluster.
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

    /// <summary>
    /// How much longer than the drain's own timeout the deploy waits for the drain Job: time to schedule the pod,
    /// pull the image and start the host, and the drain's first settle.
    /// </summary>
    public static readonly TimeSpan DrainJobSlack = TimeSpan.FromMinutes(5);

    /// <summary>How long the deploy waits for the resume Job.</summary>
    public static readonly TimeSpan ResumeTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the drain holds once it has finished waiting, for <paramref name="deployments"/> Deployments: each
    /// one's rollout, each one's again in a rollback, the resume Job and <see cref="DrainJobSlack"/>, at most a day.
    /// The deploy ends the drain as soon as it has rolled; this is when a drain a deploy that died left behind lapses.
    /// </summary>
    public static TimeSpan DrainHold(int deployments)
    {
        var hold = RolloutTimeout * (2 * deployments) + ResumeTimeout + DrainJobSlack;
        return hold < TraxDrain.MaximumTimeout ? hold : TraxDrain.MaximumTimeout;
    }

    public async Task DeployAsync(
        DeployFile file,
        string filePath,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        CancellationToken ct,
        EksTarget? eks = null,
        bool force = false
    )
    {
        var env = file.Environment(environment);
        var manifests = KubernetesTarget.Render(file, environment, exports, tag, eks, force);
        say(
            $"Deploying {file.App} to namespace {manifests.NamespaceName} in context {cluster.Context}."
        );
        await PrepareNamespaceAsync(file, environment, manifests, ct).ConfigureAwait(false);
        // On EKS the images were pushed to ECR before the foundation was deployed.
        if (eks is null)
            await PublishImagesAsync(file, filePath, env, tag).ConfigureAwait(false);

        // Before any pod: the admission policy is what refuses NET_ADMIN to every container but the egress rules.
        var admission = await ApplyEgressAdmissionAsync(file, manifests, ct).ConfigureAwait(false);
        // The network policies go first, so the in-cluster database and broker are never up without them.
        await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Policies, ct)
            .ConfigureAwait(false);
        await ProveTailnetPolicyAsync(manifests, ct).ConfigureAwait(false);
        await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Backing, ct)
            .ConfigureAwait(false);
        await MigrateAsync(environment, manifests, ct).ConfigureAwait(false);
        var proxySecretBefore = await RotateProxySecretAsync(file, manifests, ct)
            .ConfigureAwait(false);

        var before = await DeploymentsBeforeAsync(manifests, ct).ConfigureAwait(false);
        // A first deploy has nothing running to drain.
        var drained = before.Values.Any(d => d is not null);
        if (drained)
            await DrainAsync(environment, env.DrainTimeoutValue(), manifests, ct)
                .ConfigureAwait(false);

        say($"Rolling the services onto {tag}...");
        try
        {
            await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Services, ct)
                .ConfigureAwait(false);
        }
        // Any failure, the API's own (a 5xx, a refusal, a dropped connection) included: the services may be half
        // rolled and the hosts drained, so both are put right before the failure is reported.
        catch (Exception failed)
            when (failed is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            var proxySecret = proxySecretBefore is null
                ? null
                : ProxySecret(file, manifests, proxySecretBefore);
            var rolledBack = await RollBackAsync(manifests.NamespaceName, before, proxySecret, ct)
                .ConfigureAwait(false);
            var resumed = drained ? await ResumeAsync(manifests, ct).ConfigureAwait(false) : "";
            throw new InvalidOperationException(
                $"{Described(failed)} {rolledBack}{resumed}",
                failed
            );
        }
        if (drained)
        {
            var resumed = await ResumeAsync(manifests, ct).ConfigureAwait(false);
            if (resumed.Length > 0)
                say(resumed.Trim());
        }
        if (!ReferenceEquals(admission, manifests.Admission))
            await NarrowEgressAdmissionAsync(manifests, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the egress admission policy. Until the services have rolled it also allows the egress image they ran
    /// before, whose pods a roll and a rollback still create; <see cref="NarrowEgressAdmissionAsync"/> takes that
    /// away once they are up. Returns what it applied.
    /// </summary>
    private async Task<
        IReadOnlyList<k8s.IKubernetesObject<V1ObjectMeta>>
    > ApplyEgressAdmissionAsync(
        DeployFile file,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var admission = manifests.Admission;
        if (admission.Count > 0)
        {
            var before = await DeploymentsBeforeAsync(manifests, ct).ConfigureAwait(false);
            admission = KubernetesTarget.EgressAdmissionWhileRolling(
                file,
                manifests,
                KubernetesTarget.EgressImagesOf(before.Values)
            );
        }
        await ApplyAndWaitAsync(manifests.NamespaceName, admission, ct).ConfigureAwait(false);
        return admission;
    }

    /// <summary>
    /// Puts the egress admission policy back to this deploy's image alone, once the services are up and the drain has
    /// ended, so a refusal here does not leave every host idle until the drain lapses.
    /// </summary>
    private async Task NarrowEgressAdmissionAsync(
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        say("Allowing only this deploy's egress image...");
        await ApplyAndWaitAsync(manifests.NamespaceName, manifests.Admission, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// On a tailnet exposure, proves the cluster enforces the edge's NetworkPolicy before anything it protects runs:
    /// the edge adds the proxy secret to whatever reaches it, so on a plugin that ignores the policy any pod in the
    /// cluster could sign in as anyone. Starts a listener labelled as the edge, runs a probe from one of the app's own
    /// pods against it, and refuses the deploy unless the probe was kept out. The listener and its Service are deleted
    /// either way; the Job is kept a day for its log.
    /// </summary>
    private async Task ProveTailnetPolicyAsync(KubernetesManifests manifests, CancellationToken ct)
    {
        if (manifests.TailnetCanary is not { } canary)
            return;
        var ns = manifests.NamespaceName;
        var job = canary.Probe.Metadata.Name;
        say($"Proving the cluster enforces the tailnet edge's network policy (job {job})...");
        KubernetesJobResult result;
        try
        {
            await ApplyAndWaitAsync(ns, [canary.Listener, canary.Service], ct)
                .ConfigureAwait(false);
            await cluster.CreateJobAsync(canary.Probe, ct).ConfigureAwait(false);
            result = await cluster
                .WaitForJobAsync(ns, job, TimeSpan.FromMinutes(5), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (
                var o in new k8s.IKubernetesObject<V1ObjectMeta>[]
                {
                    canary.Listener,
                    canary.Service,
                }
            )
                await cluster
                    .DeleteAsync(
                        ns,
                        new KubernetesObjectState(o.Kind, o.Metadata.Name, App: null),
                        ct
                    )
                    .ConfigureAwait(false);
        }
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "The cluster does not enforce NetworkPolicy, or the probe could not tell: a pod of the app reached a "
                    + "listener labelled as the tailnet edge, whose policy admits only the Tailscale operator's proxy. "
                    + "The edge vouches for whoever reaches it, so nothing was exposed. Use a network plugin that "
                    + "enforces NetworkPolicy (Calico, Cilium, the VPC CNI's network policy agent on EKS). The end of "
                    + $"the probe's log:\n{result.LogTail}\nAll of it: 'kubectl logs -n {ns} job/{job}'."
            );
    }

    /// <summary>
    /// Runs the drain Job: every host stops starting new work, and the Job waits up to the environment's drain timeout
    /// for what is busy to finish. Refuses to go on when it failed, with the end of its log, which lists what was
    /// still busy. The Job ended the drain itself when it refused.
    /// </summary>
    private async Task DrainAsync(
        string environment,
        TimeSpan timeout,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        var ns = manifests.NamespaceName;
        var job = manifests.Drain.Metadata.Name;
        say(
            $"Draining: no host starts new work while the deploy waits up to {timeout:c} for what is busy (job {job})..."
        );
        await cluster.CreateJobAsync(manifests.Drain, ct).ConfigureAwait(false);
        var drain = await cluster
            .WaitForJobAsync(ns, job, timeout + DrainJobSlack, ct)
            .ConfigureAwait(false);
        if (!drain.Succeeded)
            throw new InvalidOperationException(
                $"The drain failed{(drain.Reason is null ? "" : $" ({drain.Reason})")}, so no service was moved to "
                    + $"the new version. The end of its log, which names any work still busy:\n{drain.LogTail}\n"
                    + "Work still busy at the timeout ends the drain, so the hosts take work again. Let it finish or "
                    + $"cancel it, raise drainTimeout, or run 'trax deploy --env {environment} --force' to roll "
                    + "anyway. A drain that failed some other way lapses on its own after the timeout and "
                    + $"{DrainHold(manifests.Services.OfType<V1Deployment>().Count()):c}, or Resume on the "
                    + $"console's scheduler page ends it. All of its log: 'kubectl logs -n {ns} job/{job}'."
            );
    }

    /// <summary>
    /// Runs the resume Job, so every host takes work again. A failure does not undo the deploy: a drain left behind
    /// lapses on its own. Returns a sentence saying so, or empty.
    /// </summary>
    private async Task<string> ResumeAsync(KubernetesManifests manifests, CancellationToken ct)
    {
        var ns = manifests.NamespaceName;
        var job = manifests.Resume.Metadata.Name;
        say($"Ending the drain (job {job})...");
        try
        {
            await cluster.CreateJobAsync(manifests.Resume, ct).ConfigureAwait(false);
            var resume = await cluster
                .WaitForJobAsync(ns, job, ResumeTimeout, ct)
                .ConfigureAwait(false);
            if (resume.Succeeded)
                return "";
            return $" Ending the drain failed{(resume.Reason is null ? "" : $" ({resume.Reason})")}: it lapses on "
                + "its own. End it now from the console's scheduler page, or see "
                + $"'kubectl logs -n {ns} job/{job}' for why.";
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            return $" Ending the drain failed ({Described(ex)}): it lapses on its own. End it now from the "
                + "console's scheduler page.";
        }
    }

    /// <summary>A failure in a sentence: what the API server answered when it refused, or the message.</summary>
    private static string Described(Exception ex) =>
        ex is k8s.Autorest.HttpOperationException http
            ? $"The cluster refused: {http.Response.StatusCode} {http.Response.Content}".TrimEnd()
            : ex.Message;

    /// <summary>
    /// On a tailnet exposure, writes a new proxy secret to its Secret, which the hub and the edge both restart onto
    /// in this rollout, and returns the value it replaced, or null. <see cref="PrepareNamespaceAsync"/> has refused a
    /// Secret of that name trax did not write.
    /// </summary>
    private async Task<string?> RotateProxySecretAsync(
        DeployFile file,
        KubernetesManifests manifests,
        CancellationToken ct
    )
    {
        if (manifests.ProxySecretName is not { } name)
            return null;
        var previous = await cluster
            .ProxySecretAsync(manifests.NamespaceName, name, ct)
            .ConfigureAwait(false);
        say($"Rotating the tailnet proxy's secret ({name})...");
        await cluster
            .ApplyAsync(ProxySecret(file, manifests, _mintProxySecret()), ct)
            .ConfigureAwait(false);
        return previous;
    }

    /// <summary>The tailnet proxy's Secret holding <paramref name="value"/>.</summary>
    private static V1Secret ProxySecret(
        DeployFile file,
        KubernetesManifests manifests,
        string value
    ) =>
        new()
        {
            ApiVersion = "v1",
            Kind = "Secret",
            Type = "Opaque",
            Metadata = new V1ObjectMeta
            {
                Name = manifests.ProxySecretName,
                NamespaceProperty = manifests.NamespaceName,
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesTarget.ManagedByLabel] = KubernetesTarget.ManagedBy,
                    [KubernetesTarget.AppLabel] = file.App,
                },
            },
            StringData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesTailnet.ProxySecretKey] = value,
            },
        };

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
    /// before it working, since that version serves while it runs. On a tailnet exposure the proxy secret this deploy
    /// replaced, <paramref name="proxySecret"/>, goes back first: the old hub still holds it, and the edge restored
    /// beside it reads the Secret as it starts. Returns the sentence the failure ends with.
    /// </summary>
    private async Task<string> RollBackAsync(
        string ns,
        IReadOnlyDictionary<string, V1Deployment?> before,
        V1Secret? proxySecret,
        CancellationToken ct
    )
    {
        if (before.Values.All(d => d is null))
            return "Nothing was rolled back: no service ran before this deploy. Fix it and deploy again.";
        say("Rolling the services back to the version before this deploy...");
        try
        {
            if (proxySecret is not null)
                await cluster.ApplyAsync(proxySecret, ct).ConfigureAwait(false);
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
        catch (Exception rollback)
            when (rollback is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            return $"Rolling back failed too, so the services are in between versions: {Described(rollback)}";
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

        foreach (var o in manifests.Admission)
            if (
                await cluster
                    .ClusterObjectAsync(o.Kind, o.Metadata.Name, ct)
                    .ConfigureAwait(false) is
                { } existing
            )
                CheckOwner(file, $"{o.Kind} {o.Metadata.Name}", existing.App, ownerless: false);

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
        await DeleteAdmissionAsync(file, ns, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Deletes the egress admission binding and policy that hold <paramref name="ns"/>, when this app wrote them: the
    /// binding first, so the policy is never left bound to a namespace that has gone.
    /// </summary>
    private async Task DeleteAdmissionAsync(DeployFile file, string ns, CancellationToken ct)
    {
        foreach (
            var kind in new[] { "ValidatingAdmissionPolicyBinding", "ValidatingAdmissionPolicy" }
        )
            if (
                await cluster
                    .ClusterObjectAsync(kind, KubernetesTarget.EgressAdmissionName(ns), ct)
                    .ConfigureAwait(false)
                    is { } admission
                && string.Equals(admission.App, file.App, StringComparison.Ordinal)
            )
            {
                say($"Deleting {kind} {admission.Name}...");
                await cluster.DeleteAsync(ns, admission, ct).ConfigureAwait(false);
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
