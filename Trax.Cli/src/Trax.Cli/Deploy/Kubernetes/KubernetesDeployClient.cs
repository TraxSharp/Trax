using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// The cluster calls, made with the official Kubernetes client against the current context of the kubeconfig
/// (<c>KUBECONFIG</c>, else <c>~/.kube/config</c>). Objects are applied server-side under the field manager
/// <c>trax</c>.
/// </summary>
internal sealed class KubernetesDeployClient : IKubernetesDeployClient
{
    private const string FieldManager = "trax";
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    private readonly k8s.Kubernetes _client;
    private readonly TimeProvider _clock;

    public KubernetesDeployClient(TimeProvider clock)
    {
        _clock = clock;
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        Context = config.CurrentContext ?? "(none)";
        _client = new k8s.Kubernetes(config);
    }

    public string Context { get; }

    public async Task<IReadOnlyDictionary<string, string>?> NamespaceLabelsAsync(
        string name,
        CancellationToken ct
    )
    {
        var ns = await OrNull(() => _client.CoreV1.ReadNamespaceAsync(name, cancellationToken: ct))
            .ConfigureAwait(false);
        return ns is null
            ? null
            : new Dictionary<string, string>(
                ns.Metadata.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
                StringComparer.Ordinal
            );
    }

    public Task CreateNamespaceAsync(V1Namespace body, CancellationToken ct) =>
        _client.CoreV1.CreateNamespaceAsync(
            body,
            fieldManager: FieldManager,
            cancellationToken: ct
        );

    public async Task<IReadOnlyCollection<string>?> SecretKeysAsync(
        string ns,
        string name,
        CancellationToken ct
    )
    {
        var secret = await OrNull(() =>
                _client.CoreV1.ReadNamespacedSecretAsync(name, ns, cancellationToken: ct)
            )
            .ConfigureAwait(false);
        if (secret is null)
            return null;
        // Only the names leave this method; the values are dropped here.
        return
        [
            .. (secret.Data?.Keys ?? [])
                .Concat(secret.StringData?.Keys ?? [])
                .Distinct(StringComparer.Ordinal),
        ];
    }

    public async Task<IReadOnlyList<KubernetesObjectState>> ListAsync(
        string ns,
        CancellationToken ct
    )
    {
        static KubernetesObjectState State(string kind, V1ObjectMeta meta) =>
            new(
                kind,
                meta.Name,
                meta.Labels is { } labels
                && labels.TryGetValue(KubernetesTarget.AppLabel, out var app)
                    ? app
                    : null
            );

        var all = new List<KubernetesObjectState>();
        all.AddRange(
            (
                await _client
                    .AppsV1.ListNamespacedDeploymentAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("Deployment", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .AppsV1.ListNamespacedStatefulSetAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("StatefulSet", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .CoreV1.ListNamespacedServiceAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("Service", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .BatchV1.ListNamespacedJobAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("Job", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .CoreV1.ListNamespacedPersistentVolumeClaimAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("PersistentVolumeClaim", o.Metadata))
        );
        return all;
    }

    public async Task ApplyAsync(IKubernetesObject<V1ObjectMeta> body, CancellationToken ct)
    {
        var patch = new V1Patch(body, V1Patch.PatchType.ApplyPatch);
        var name = body.Metadata.Name;
        var ns = body.Metadata.NamespaceProperty;
        switch (body)
        {
            case V1Service:
                await _client
                    .CoreV1.PatchNamespacedServiceAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                break;
            case V1Deployment:
                await _client
                    .AppsV1.PatchNamespacedDeploymentAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                break;
            case V1StatefulSet:
                await _client
                    .AppsV1.PatchNamespacedStatefulSetAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"trax does not apply a {body.Kind}.");
        }
    }

    public Task CreateJobAsync(V1Job body, CancellationToken ct) =>
        _client.BatchV1.CreateNamespacedJobAsync(
            body,
            body.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            cancellationToken: ct
        );

    public async Task<KubernetesJobResult> WaitForJobAsync(
        string ns,
        string name,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            var job = await _client
                .BatchV1.ReadNamespacedJobStatusAsync(name, ns, cancellationToken: ct)
                .ConfigureAwait(false);
            if (job.Status?.Succeeded > 0)
                return new KubernetesJobResult(Succeeded: true, Reason: null, LogTail: "");
            var failed = job.Status?.Conditions?.FirstOrDefault(c =>
                string.Equals(c.Type, "Failed", StringComparison.Ordinal)
                && string.Equals(c.Status, "True", StringComparison.Ordinal)
            );
            if (failed is not null || job.Status?.Failed > 0)
                return new KubernetesJobResult(
                    Succeeded: false,
                    failed?.Reason ?? failed?.Message,
                    await LogTailAsync(ns, $"job-name={name}", ct).ConfigureAwait(false)
                );
            if (_clock.GetUtcNow() > deadline)
                return new KubernetesJobResult(
                    Succeeded: false,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"still running after {timeout.TotalMinutes:0} minutes"
                    ),
                    await LogTailAsync(ns, $"job-name={name}", ct).ConfigureAwait(false)
                );
            await Task.Delay(Poll, _clock, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> LogTailAsync(string ns, string selector, CancellationToken ct)
    {
        var pods = await _client
            .CoreV1.ListNamespacedPodAsync(ns, labelSelector: selector, cancellationToken: ct)
            .ConfigureAwait(false);
        var tail = new List<string>();
        foreach (var pod in pods.Items)
        {
            var log = await OrNull(() =>
                    _client.CoreV1.ReadNamespacedPodLogAsync(
                        pod.Metadata.Name,
                        ns,
                        tailLines: 50,
                        cancellationToken: ct
                    )
                )
                .ConfigureAwait(false);
            if (log is null)
            {
                var waiting = pod.Status?.ContainerStatuses?.FirstOrDefault()?.State?.Waiting;
                tail.Add(
                    $"[{pod.Metadata.Name}] no log ({waiting?.Reason ?? pod.Status?.Phase}: {waiting?.Message})"
                );
                continue;
            }
            using var reader = new StreamReader(log);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                tail.Add($"[{pod.Metadata.Name}] {line}");
        }
        return tail.Count == 0 ? "(no pod of the job left a log)" : string.Join('\n', tail);
    }

    public async Task WaitForRolloutAsync(
        string ns,
        string kind,
        string name,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            var (done, state) = kind switch
            {
                "Deployment" => Rolled(
                    await _client
                        .AppsV1.ReadNamespacedDeploymentStatusAsync(name, ns, cancellationToken: ct)
                        .ConfigureAwait(false)
                ),
                "StatefulSet" => Rolled(
                    await _client
                        .AppsV1.ReadNamespacedStatefulSetStatusAsync(
                            name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false)
                ),
                _ => throw new InvalidOperationException($"trax does not wait on a {kind}."),
            };
            if (done)
                return;
            if (_clock.GetUtcNow() > deadline)
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{kind} {name} did not roll out within {timeout.TotalMinutes:0} minutes ({state}). See "
                    ) + $"'kubectl describe {kind.ToLowerInvariant()} {name} -n {ns}'."
                );
            await Task.Delay(Poll, _clock, ct).ConfigureAwait(false);
        }
    }

    private static (bool, string) Rolled(V1Deployment d)
    {
        var want = d.Spec.Replicas ?? 1;
        var s = d.Status;
        var done =
            s is not null
            && s.ObservedGeneration >= d.Metadata.Generation
            && (s.UpdatedReplicas ?? 0) == want
            && (s.AvailableReplicas ?? 0) == want
            && (s.Replicas ?? 0) == want;
        return (
            done,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{s?.AvailableReplicas ?? 0}/{want} available, {s?.UpdatedReplicas ?? 0} updated"
            )
        );
    }

    private static (bool, string) Rolled(V1StatefulSet set)
    {
        var want = set.Spec.Replicas ?? 1;
        var s = set.Status;
        var done =
            s is not null
            && s.ObservedGeneration >= set.Metadata.Generation
            && (s.ReadyReplicas ?? 0) == want
            && (s.UpdatedReplicas ?? 0) == want;
        return (
            done,
            string.Create(CultureInfo.InvariantCulture, $"{s?.ReadyReplicas ?? 0}/{want} ready")
        );
    }

    public async Task<IReadOnlyList<KubernetesWorkloadState>> WorkloadsAsync(
        string ns,
        string app,
        CancellationToken ct
    )
    {
        var selector = $"{KubernetesTarget.AppLabel}={app}";
        var all = new List<KubernetesWorkloadState>();
        foreach (
            var d in (
                await _client
                    .AppsV1.ListNamespacedDeploymentAsync(
                        ns,
                        labelSelector: selector,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false)
            ).Items
        )
            all.Add(
                new(
                    "Deployment",
                    d.Metadata.Name,
                    d.Status?.ReadyReplicas ?? 0,
                    d.Spec.Replicas ?? 1,
                    d.Spec.Template.Spec.Containers.FirstOrDefault()?.Image
                )
            );
        foreach (
            var s in (
                await _client
                    .AppsV1.ListNamespacedStatefulSetAsync(
                        ns,
                        labelSelector: selector,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false)
            ).Items
        )
            all.Add(
                new(
                    "StatefulSet",
                    s.Metadata.Name,
                    s.Status?.ReadyReplicas ?? 0,
                    s.Spec.Replicas ?? 1,
                    s.Spec.Template.Spec.Containers.FirstOrDefault()?.Image
                )
            );
        return all;
    }

    public async IAsyncEnumerable<string> LogsAsync(
        string ns,
        string app,
        string service,
        bool follow,
        int sinceMinutes,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var pods = (
            await _client
                .CoreV1.ListNamespacedPodAsync(
                    ns,
                    labelSelector: $"{KubernetesTarget.NameLabel}={service},{KubernetesTarget.AppLabel}={app}",
                    cancellationToken: ct
                )
                .ConfigureAwait(false)
        ).Items;
        var lines = Channel.CreateUnbounded<string>();
        var readers = pods.Select(async pod =>
            {
                var log = await OrNull(() =>
                        _client.CoreV1.ReadNamespacedPodLogAsync(
                            pod.Metadata.Name,
                            ns,
                            follow: follow,
                            sinceSeconds: sinceMinutes * 60,
                            cancellationToken: ct
                        )
                    )
                    .ConfigureAwait(false);
                if (log is null)
                    return;
                using var reader = new StreamReader(log);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                    await lines
                        .Writer.WriteAsync($"[{pod.Metadata.Name}] {line}", ct)
                        .ConfigureAwait(false);
            })
            .ToList();
        _ = Task.WhenAll(readers)
            .ContinueWith(t => lines.Writer.TryComplete(t.Exception), TaskScheduler.Default);
        await foreach (var line in lines.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return line;
    }

    public async Task DeleteAsync(string ns, KubernetesObjectState target, CancellationToken ct)
    {
        const string background = "Background";
        await OrNull<object>(async () =>
                target.Kind switch
                {
                    "Deployment" => await _client
                        .AppsV1.DeleteNamespacedDeploymentAsync(
                            target.Name,
                            ns,
                            propagationPolicy: background,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "StatefulSet" => await _client
                        .AppsV1.DeleteNamespacedStatefulSetAsync(
                            target.Name,
                            ns,
                            propagationPolicy: background,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "Job" => await _client
                        .BatchV1.DeleteNamespacedJobAsync(
                            target.Name,
                            ns,
                            propagationPolicy: background,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "Service" => await _client
                        .CoreV1.DeleteNamespacedServiceAsync(target.Name, ns, cancellationToken: ct)
                        .ConfigureAwait(false),
                    "PersistentVolumeClaim" => await _client
                        .CoreV1.DeleteNamespacedPersistentVolumeClaimAsync(
                            target.Name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        $"trax does not delete a {target.Kind}."
                    ),
                }
            )
            .ConfigureAwait(false);
    }

    /// <summary>The call's result, or null when the object does not exist.</summary>
    private static async Task<T?> OrNull<T>(Func<Task<T>> call)
        where T : class
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
