using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
        : this(KubernetesClientConfiguration.BuildConfigFromConfigFile(), clock) { }

    /// <summary>A client for <paramref name="config"/>; tests pass a handler that answers for the API server.</summary>
    internal KubernetesDeployClient(
        KubernetesClientConfiguration config,
        TimeProvider clock,
        params DelegatingHandler[] handlers
    )
    {
        _clock = clock;
        Context = config.CurrentContext ?? "(none)";
        Server = config.Host ?? "";
        _client = new k8s.Kubernetes(config, handlers);
    }

    public string Context { get; }

    public string Server { get; }

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

    public Task<V1Deployment?> ReadDeploymentAsync(string ns, string name, CancellationToken ct) =>
        OrNull(() => _client.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct));

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

    public async Task<IReadOnlyDictionary<string, string>?> SecretLabelsAsync(
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
        // Only the labels leave this method; the values are dropped here.
        return new Dictionary<string, string>(
            secret.Metadata.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal
        );
    }

    public async Task<IReadOnlyList<KubernetesObjectState>> ListAsync(
        string ns,
        CancellationToken ct
    )
    {
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
                    .CoreV1.ListNamespacedServiceAccountAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("ServiceAccount", o.Metadata))
        );
        all.AddRange(await SecurityGroupPoliciesAsync(ns, ct).ConfigureAwait(false));
        all.AddRange(await AroundAsync(ns, ct).ConfigureAwait(false));
        all.AddRange(
            (
                await _client
                    .CoreV1.ListNamespacedPersistentVolumeClaimAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("PersistentVolumeClaim", o.Metadata))
        );
        return all;
    }

    private static KubernetesObjectState State(string kind, V1ObjectMeta meta) =>
        new(
            kind,
            meta.Name,
            meta.Labels is { } labels && labels.TryGetValue(KubernetesTarget.AppLabel, out var app)
                ? app
                : null
        );

    /// <summary>The namespace's NetworkPolicies, Ingresses and ConfigMaps.</summary>
    private async Task<List<KubernetesObjectState>> AroundAsync(string ns, CancellationToken ct)
    {
        var all = new List<KubernetesObjectState>();
        all.AddRange(
            (
                await _client
                    .NetworkingV1.ListNamespacedNetworkPolicyAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("NetworkPolicy", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .NetworkingV1.ListNamespacedIngressAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("Ingress", o.Metadata))
        );
        all.AddRange(
            (
                await _client
                    .CoreV1.ListNamespacedConfigMapAsync(ns, cancellationToken: ct)
                    .ConfigureAwait(false)
            ).Items.Select(o => State("ConfigMap", o.Metadata))
        );
        return all;
    }

    /// <summary>
    /// The namespace's SecurityGroupPolicies, by name and app label. A cluster without the VPC resource controller has
    /// no such kind, and so none.
    /// </summary>
    private async Task<List<KubernetesObjectState>> SecurityGroupPoliciesAsync(
        string ns,
        CancellationToken ct
    )
    {
        var found = new List<KubernetesObjectState>();
        JsonElement json;
        try
        {
            json = await _client
                .CustomObjects.ListNamespacedCustomObjectAsync<JsonElement>(
                    EksSecurityGroupPolicy.Group,
                    EksSecurityGroupPolicy.Version,
                    ns,
                    EksSecurityGroupPolicy.Plural,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return found;
        }
        if (!json.TryGetProperty("items", out var items))
            return found;
        foreach (var item in items.EnumerateArray())
        {
            var meta = item.GetProperty("metadata");
            var app =
                meta.TryGetProperty("labels", out var labels)
                && labels.TryGetProperty(KubernetesTarget.AppLabel, out var label)
                    ? label.GetString()
                    : null;
            found.Add(
                new KubernetesObjectState(
                    "SecurityGroupPolicy",
                    meta.GetProperty("name").GetString() ?? "",
                    app
                )
            );
        }
        return found;
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
                await ApplyIdentityAsync(body, patch, name, ns, ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>What a deploy applies around the workloads: its network policies, a tailnet exposure's objects.</summary>
    private async Task<bool> ApplyAroundAsync(
        IKubernetesObject<V1ObjectMeta> body,
        V1Patch patch,
        string name,
        string ns,
        CancellationToken ct
    )
    {
        switch (body)
        {
            case V1NetworkPolicy:
                await _client
                    .NetworkingV1.PatchNamespacedNetworkPolicyAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                return true;
            case V1Ingress:
                await _client
                    .NetworkingV1.PatchNamespacedIngressAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                return true;
            case V1ConfigMap:
                await _client
                    .CoreV1.PatchNamespacedConfigMapAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                return true;
            case V1Secret:
                await _client
                    .CoreV1.PatchNamespacedSecretAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A ServiceAccount, or an EKS SecurityGroupPolicy: what a deploy to EKS applies beside the workloads.</summary>
    private async Task ApplyIdentityAsync(
        IKubernetesObject<V1ObjectMeta> body,
        V1Patch patch,
        string name,
        string ns,
        CancellationToken ct
    )
    {
        if (await ApplyAroundAsync(body, patch, name, ns, ct).ConfigureAwait(false))
            return;
        switch (body)
        {
            case V1ServiceAccount:
                await _client
                    .CoreV1.PatchNamespacedServiceAccountAsync(
                        patch,
                        name,
                        ns,
                        fieldManager: FieldManager,
                        force: true,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                break;
            case EksSecurityGroupPolicy:
                await _client
                    .CustomObjects.PatchNamespacedCustomObjectAsync(
                        patch,
                        EksSecurityGroupPolicy.Group,
                        EksSecurityGroupPolicy.Version,
                        ns,
                        EksSecurityGroupPolicy.Plural,
                        name,
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
            var stuck = await StuckAsync(ns, name, ct).ConfigureAwait(false);
            if (stuck is not null || _clock.GetUtcNow() > deadline)
                return await GiveUpAsync(
                        ns,
                        name,
                        stuck
                            ?? string.Create(
                                CultureInfo.InvariantCulture,
                                $"still running after {timeout.TotalMinutes:0} minutes"
                            ),
                        ct
                    )
                    .ConfigureAwait(false);
            await Task.Delay(Poll, _clock, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Why the Job's pod can never start, or null. A Job with no retries does not fail while its pod waits on an
    /// image it cannot pull, so without this the wait would run to its deadline.
    /// </summary>
    private async Task<string?> StuckAsync(string ns, string job, CancellationToken ct)
    {
        var pods = await _client
            .CoreV1.ListNamespacedPodAsync(
                ns,
                labelSelector: $"job-name={job}",
                cancellationToken: ct
            )
            .ConfigureAwait(false);
        return pods
            .Items.Select(Waiting)
            .Where(w => w?.Reason is { } reason && NeverStarts.Contains(reason))
            .Select(w => $"{w?.Reason}: {w?.Message}".TrimEnd(' ', ':'))
            .FirstOrDefault();
    }

    /// <summary>Reads the end of the Job's log, then deletes the Job so its pod stops retrying.</summary>
    private async Task<KubernetesJobResult> GiveUpAsync(
        string ns,
        string job,
        string reason,
        CancellationToken ct
    )
    {
        var tail = await LogTailAsync(ns, $"job-name={job}", ct).ConfigureAwait(false);
        await OrNull(() =>
                _client.BatchV1.DeleteNamespacedJobAsync(
                    job,
                    ns,
                    propagationPolicy: "Background",
                    cancellationToken: ct
                )
            )
            .ConfigureAwait(false);
        return new KubernetesJobResult(Succeeded: false, reason, tail);
    }

    /// <summary>
    /// Why a pod of the workload can never start, or null: a container waiting on something that does not fix itself
    /// (an image it cannot pull, a Secret key it cannot read), or a sandbox the node has failed to create
    /// <see cref="SandboxAttempts"/> times. The second is what a cluster that cannot give a pod a user namespace of
    /// its own (<c>hostUsers: false</c>) does with every Trax pod: kind, whose nodes are containers, is one. Without
    /// this the wait runs to its ten minutes and says only that the rollout did not finish.
    /// </summary>
    private async Task<string?> CannotStartAsync(string ns, string selector, CancellationToken ct)
    {
        var pods = await _client
            .CoreV1.ListNamespacedPodAsync(ns, labelSelector: selector, cancellationToken: ct)
            .ConfigureAwait(false);
        var waiting = pods
            .Items.Select(Waiting)
            .Where(w => w?.Reason is { } reason && NeverStarts.Contains(reason))
            .Select(w => $"{w?.Reason}: {w?.Message}".TrimEnd(' ', ':'))
            .FirstOrDefault();
        if (waiting is not null)
            return waiting;
        foreach (var pod in pods.Items)
        {
            var events = await _client
                .CoreV1.ListNamespacedEventAsync(
                    ns,
                    fieldSelector: $"involvedObject.name={pod.Metadata.Name},reason={SandboxFailed}",
                    cancellationToken: ct
                )
                .ConfigureAwait(false);
            if (SandboxFailure(events.Items) is { } failure)
                return failure;
        }
        return null;
    }

    /// <summary>The event reason a kubelet reports when it cannot create a pod's sandbox.</summary>
    internal const string SandboxFailed = "FailedCreatePodSandBox";

    /// <summary>How many failed sandbox attempts mean the node will never create it, not that it is still trying.</summary>
    internal const int SandboxAttempts = 3;

    /// <summary>
    /// The sentence for a pod whose sandbox the node has failed to create <see cref="SandboxAttempts"/> times or more
    /// (an event's count, or its series' when the kubelet groups them), or null.
    /// </summary>
    internal static string? SandboxFailure(IEnumerable<Corev1Event> events)
    {
        var failed = events
            .Where(e => string.Equals(e.Reason, SandboxFailed, StringComparison.Ordinal))
            .ToList();
        var attempts = failed.Sum(e => Math.Max(e.Count ?? 1, e.Series?.Count ?? 0));
        if (attempts < SandboxAttempts)
            return null;
        var message = failed[^1].Message ?? "";
        return
            message.Contains("sysfs", StringComparison.Ordinal)
            || message.Contains("user namespace", StringComparison.OrdinalIgnoreCase)
            ? $"the node cannot give it a user namespace of its own, which every Trax pod asks for with hostUsers: "
                + $"false. kind cannot (its nodes are containers); k3s, EKS and a kernel of 6.3 or later can. {message}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"the node failed {attempts} times to create its sandbox: {message}"
            );
    }

    /// <summary>The waiting reasons a container does not recover from without a change to the Job.</summary>
    private static readonly HashSet<string> NeverStarts = new(StringComparer.Ordinal)
    {
        "ImagePullBackOff",
        "ErrImagePull",
        "CreateContainerConfigError",
        "InvalidImageName",
    };

    private async Task<string> LogTailAsync(string ns, string selector, CancellationToken ct)
    {
        var pods = await _client
            .CoreV1.ListNamespacedPodAsync(ns, labelSelector: selector, cancellationToken: ct)
            .ConfigureAwait(false);
        var tail = new List<string>();
        foreach (var pod in pods.Items)
        {
            var log = await NotStarted(() =>
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
                var waiting = Waiting(pod);
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
            if (
                await CannotStartAsync(ns, $"{KubernetesTarget.NameLabel}={name}", ct)
                    .ConfigureAwait(false) is
                { } why
            )
                throw new InvalidOperationException(
                    $"{kind} {name} did not roll out: its pod cannot start ({why}). See "
                        + $"'kubectl describe pods -l {KubernetesTarget.NameLabel}={name} -n {ns}'."
                );
            if (state.StartsWith(PastDeadline, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{kind} {name} did not roll out: {state}. See "
                        + $"'kubectl describe {kind.ToLowerInvariant()} {name} -n {ns}'."
                );
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

    /// <summary>How <see cref="Rolled(V1Deployment)"/> starts the state of a Deployment past its progress deadline.</summary>
    internal const string PastDeadline = "past its progress deadline";

    internal static (bool Done, string State) Rolled(V1Deployment d)
    {
        var want = d.Spec.Replicas ?? 1;
        var s = d.Status;
        var stalled = s?.Conditions?.FirstOrDefault(c =>
            string.Equals(c.Type, "Progressing", StringComparison.Ordinal)
            && string.Equals(c.Status, "False", StringComparison.Ordinal)
            && string.Equals(c.Reason, "ProgressDeadlineExceeded", StringComparison.Ordinal)
        );
        if (stalled is not null && s?.ObservedGeneration >= d.Metadata.Generation)
            return (false, $"{PastDeadline} ({stalled.Message})");
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

    internal static (bool Done, string State) Rolled(V1StatefulSet set)
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
        if (pods.Count == 0)
            throw new InvalidOperationException(
                $"No pod of '{service}' is in namespace {ns}. A migrate Job's pods go a day after it ends; "
                    + "'trax deploy status' shows what is running."
            );
        var lines = Channel.CreateUnbounded<string>();
        var readers = pods.Select(pod => ReadPodLogAsync(pod, ns, follow, sinceMinutes, lines, ct))
            .ToList();
        _ = Task.WhenAll(readers)
            .ContinueWith(
                t => lines.Writer.TryComplete(t.Exception?.InnerException),
                TaskScheduler.Default
            );
        await foreach (var line in lines.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return line;
    }

    /// <summary>
    /// Copies one pod's log into <paramref name="lines"/>. A log the API will not give (the container has not
    /// started, or reading logs is not allowed) becomes one line saying why, so the other pods are still read.
    /// </summary>
    private async Task ReadPodLogAsync(
        V1Pod pod,
        string ns,
        bool follow,
        int sinceMinutes,
        Channel<string> lines,
        CancellationToken ct
    )
    {
        Stream log;
        try
        {
            log = await _client
                .CoreV1.ReadNamespacedPodLogAsync(
                    pod.Metadata.Name,
                    ns,
                    follow: follow,
                    sinceSeconds: sinceMinutes * 60,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
        {
            await lines
                .Writer.WriteAsync(
                    $"[{pod.Metadata.Name}] no log ({Waiting(pod)?.Reason ?? ex.Response.StatusCode.ToString()}: "
                        + $"{ApiMessage(ex)})",
                    ct
                )
                .ConfigureAwait(false);
            return;
        }
        using var reader = new StreamReader(log);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            await lines
                .Writer.WriteAsync($"[{pod.Metadata.Name}] {line}", ct)
                .ConfigureAwait(false);
    }

    private static V1ContainerStateWaiting? Waiting(V1Pod pod) =>
        pod.Status?.ContainerStatuses?.FirstOrDefault()?.State?.Waiting;

    /// <summary>The message the API server gave with a refusal, or its status when it gave none.</summary>
    private static string ApiMessage(HttpOperationException ex)
    {
        try
        {
            using var body = JsonDocument.Parse(ex.Response.Content ?? "");
            if (
                body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text
            )
                return text;
            return ex.Response.StatusCode.ToString();
        }
        catch (JsonException)
        {
            return ex.Response.StatusCode.ToString();
        }
    }

    public async Task DeleteAsync(string ns, KubernetesObjectState target, CancellationToken ct)
    {
        if (target.Kind is "ServiceAccount" or "SecurityGroupPolicy")
        {
            await DeleteIdentityAsync(ns, target, ct).ConfigureAwait(false);
            return;
        }
        if (target.Kind is "NetworkPolicy" or "Ingress" or "ConfigMap" or "Secret")
        {
            await DeleteAroundAsync(ns, target, ct).ConfigureAwait(false);
            return;
        }
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

    /// <summary>
    /// A pod's log, or null when there is none to read: the pod is gone (404), or its container has not started
    /// (400).
    /// </summary>
    private static async Task<Stream?> NotStarted(Func<Task<Stream>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
            when (ex.Response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
    }

    /// <summary>A network policy, or a tailnet exposure's Ingress, ConfigMap or proxy Secret.</summary>
    private async Task DeleteAroundAsync(
        string ns,
        KubernetesObjectState target,
        CancellationToken ct
    ) =>
        await OrNull<object>(async () =>
                target.Kind switch
                {
                    "NetworkPolicy" => await _client
                        .NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                            target.Name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "Ingress" => await _client
                        .NetworkingV1.DeleteNamespacedIngressAsync(
                            target.Name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "ConfigMap" => await _client
                        .CoreV1.DeleteNamespacedConfigMapAsync(
                            target.Name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "Secret" => await _client
                        .CoreV1.DeleteNamespacedSecretAsync(target.Name, ns, cancellationToken: ct)
                        .ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        $"trax does not delete a {target.Kind}."
                    ),
                }
            )
            .ConfigureAwait(false);

    private async Task DeleteIdentityAsync(
        string ns,
        KubernetesObjectState target,
        CancellationToken ct
    ) =>
        await OrNull<object>(async () =>
                target.Kind switch
                {
                    "ServiceAccount" => await _client
                        .CoreV1.DeleteNamespacedServiceAccountAsync(
                            target.Name,
                            ns,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    "SecurityGroupPolicy" => await _client
                        .CustomObjects.DeleteNamespacedCustomObjectAsync(
                            EksSecurityGroupPolicy.Group,
                            EksSecurityGroupPolicy.Version,
                            ns,
                            EksSecurityGroupPolicy.Plural,
                            target.Name,
                            cancellationToken: ct
                        )
                        .ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        $"trax does not delete a {target.Kind}."
                    ),
                }
            )
            .ConfigureAwait(false);

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
