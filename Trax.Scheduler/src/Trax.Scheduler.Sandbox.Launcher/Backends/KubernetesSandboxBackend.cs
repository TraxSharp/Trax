using System.Globalization;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// Runs a sandbox as a Job in the dedicated namespace, through a ServiceAccount that may create,
/// read and delete Jobs and read pods there, and nothing else: no <c>pods/exec</c>, no
/// <c>pods/log</c>.
/// </summary>
/// <remarks>
/// The input goes through a volume the launcher's own pod mounts: each run gets a directory on it,
/// whose input directory the sandbox mounts read-only, by subPath, so it sees nothing of other
/// runs. The output goes to a volume of the run's own, a generic ephemeral volume of its declared
/// disk on a storage class that enforces capacity, made with the sandbox's pod and deleted with it.
/// Once the sandbox has exited, and before its pod is deleted, a collector Job of the launcher's
/// image mounts that volume read-only, checks it entry by entry, and writes a tar to the run's
/// directory, which the launcher reads with the same checks. The runs volume keeps a free-space
/// floor, <see cref="KubernetesBackendOptions.RunsMinFreePercent"/>: below it no run starts and
/// every running one is killed. See scheduler/0021.
/// </remarks>
internal sealed class KubernetesSandboxBackend(
    IKubernetes client,
    SandboxLauncherOptions options,
    TimeProvider time,
    ILogger<KubernetesSandboxBackend> logger,
    Func<string, (long Available, long Total)>? space = null
) : ISandboxBackend
{
    private const long MiB = 1024 * 1024;

    private readonly Func<string, (long Available, long Total)> _space = space ?? FreeSpace;

    /// <summary>How long past its timeout a Job may take to be scheduled, pulled and started before the launcher gives up on it.</summary>
    internal static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(5);

    private KubernetesBackendOptions Kube => options.Kubernetes;

    public IReadOnlyList<string> CanaryProbes
    {
        get
        {
            var probes = new List<string>();
            if (
                Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is { Length: > 0 } api
            )
                probes.Add(
                    $"{api}:{Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "443"}"
                );
            if (
                Environment.GetEnvironmentVariable("POD_IP") is { Length: > 0 } self
                && options.ListenUrl is { } url
            )
                probes.Add(
                    string.Create(CultureInfo.InvariantCulture, $"{self}:{new Uri(url).Port}")
                );
            return probes;
        }
    }

    public bool LoopbackOnly => false;

    /// <summary>
    /// Refuses the start unless <see cref="KubernetesBackendOptions.OutputStorageClassName"/>
    /// exists and is not a host-path provisioner, which enforces no size. Whether any other class
    /// enforces capacity is the deploy's word: nothing the launcher can read says so.
    /// </summary>
    public async Task PreflightAsync(CancellationToken ct)
    {
        var name = Kube.OutputStorageClassName;
        V1StorageClass storage;
        try
        {
            storage = await client
                .StorageV1.ReadStorageClassAsync(name, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
            when (ex.Response.StatusCode
                    is System.Net.HttpStatusCode.NotFound
                        or System.Net.HttpStatusCode.Forbidden
            )
        {
            throw new SandboxRuntimeRefusedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The output storage class '{name}' could not be read ({(int)ex.Response.StatusCode}): "
                )
                    + "it does not exist, or the launcher's ClusterRole to read it is missing. Each run's "
                    + "output volume is made on it, as the run's disk ceiling.",
                ex
            );
        }
        if (KubernetesStorage.HostPathProvisioners.Contains(storage.Provisioner ?? ""))
            throw new SandboxRuntimeRefusedException(
                $"The output storage class '{name}' is provisioned by {storage.Provisioner}, which "
                    + "makes a directory on the node and enforces no size, so a run's output volume "
                    + "there is no disk ceiling. Name a class that enforces capacity (TopoLVM, "
                    + "Longhorn, a cloud block-storage driver)."
            );
    }

    public async Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct)
    {
        var run = Path.Combine(Kube.RunsPath, job.RunId);
        var name = KubernetesJobSpec.JobName(job.RunId);
        var collector = KubernetesJobSpec.CollectorName(job.RunId);
        try
        {
            if (LowOnSpace() is { } low)
                return SandboxExecution.Failure(low);
            await PrepareAsync(run, job, ct).ConfigureAwait(false);
            await client
                .BatchV1.CreateNamespacedJobAsync(
                    KubernetesJobSpec.Build(job, Kube),
                    Kube.Namespace,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);

            var (end, code, detail, pod) = await WatchAsync(name, job.Limits, ct)
                .ConfigureAwait(false);
            if (end != SandboxEnd.Exited)
            {
                // A killed run hands nothing back, so its output is never collected.
                if (detail is not null)
                    logger.LogError("Sandbox run {RunId} was killed: {Detail}.", job.RunId, detail);
                return new SandboxExecution(
                    end,
                    code,
                    Output: [],
                    OutputRefused: null,
                    Detail: detail
                );
            }
            if (pod is null)
                return SandboxExecution.Failure(
                    "the sandbox's pod was gone before its output was collected"
                );

            var (files, refused) = await CollectAsync(job, collector, pod, run, ct)
                .ConfigureAwait(false);
            return new SandboxExecution(end, code, files, refused);
        }
        catch (Exception ex)
            when (ex
                    is HttpOperationException
                        or HttpRequestException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            logger.LogError(
                ex,
                "Sandbox run {RunId} could not be started or collected.",
                job.RunId
            );
            return SandboxExecution.Failure(ex.Message);
        }
        finally
        {
            await CleanUpAsync(name, collector, run).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the collector against the exited sandbox's output volume, which lasts as long as its
    /// pod, and reads what it wrote. The sandbox's pod has ended and never runs again
    /// (<c>restartPolicy: Never</c>, no retries), so nothing writes the volume while it is read.
    /// </summary>
    private async Task<(IReadOnlyList<SandboxFile>, string?)> CollectAsync(
        SandboxJob job,
        string collector,
        string pod,
        string run,
        CancellationToken ct
    )
    {
        await client
            .BatchV1.CreateNamespacedJobAsync(
                KubernetesJobSpec.BuildCollector(
                    job,
                    KubernetesJobSpec.OutputClaimName(pod),
                    Kube,
                    options.Caps
                ),
                Kube.Namespace,
                cancellationToken: ct
            )
            .ConfigureAwait(false);

        var giveUp = time.GetUtcNow() + StartGrace;
        while (true)
        {
            await Task.Delay(Kube.PollInterval, time, ct).ConfigureAwait(false);
            var state = await client
                .BatchV1.ReadNamespacedJobAsync(collector, Kube.Namespace, cancellationToken: ct)
                .ConfigureAwait(false);
            var ended = Ended(state);
            if (string.Equals(ended, "Complete", StringComparison.Ordinal))
                break;
            if (ended is not null)
                throw new IOException($"The collector of run {job.RunId} failed ({ended}).");
            if (time.GetUtcNow() > giveUp)
                throw new IOException($"The collector of run {job.RunId} did not finish in time.");
        }

        return await SandboxOutputCollector
            .ReadAsync(
                Path.Combine(run, KubernetesJobSpec.Collected),
                PodmanSandboxBackend.OutputLimits(options.Caps),
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>Removes the run's Jobs and directory, on an uncancellable token: a run whose caller gave up must not leave any behind.</summary>
    private async Task CleanUpAsync(string name, string collector, string run)
    {
        // The directory only once both pods are gone: until then either can still change it
        // under the walk that removes it.
        var collectorGone = await QuietlyAsync(() =>
                DeleteAndWaitAsync(collector, CancellationToken.None)
            )
            .ConfigureAwait(false);
        var sandboxGone = await QuietlyAsync(() => DeleteAndWaitAsync(name, CancellationToken.None))
            .ConfigureAwait(false);
        if (collectorGone && sandboxGone)
            await QuietlyAsync(() =>
                {
                    RemoveRun(run);
                    return Task.CompletedTask;
                })
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a run's directory, first giving the launcher write and search on every real
    /// directory in it, since the sandbox may have left one it made read-only. Never follows a link.
    /// </summary>
    internal static void RemoveRun(string run)
    {
        if (!Directory.Exists(run))
            return;
        if (!OperatingSystem.IsWindows())
            Unlock(new DirectoryInfo(run));
        Directory.Delete(run, recursive: true);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void Unlock(DirectoryInfo root)
    {
        const UnixFileMode Owner =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = true,
        };
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            try
            {
                // One the launcher does not own stays as it is, and the delete then says so.
                directory.UnixFileMode |= Owner;
                foreach (var sub in directory.EnumerateDirectories("*", options))
                    if (sub.LinkTarget is null)
                        pending.Push(sub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task PrepareAsync(string run, SandboxJob job, CancellationToken ct)
    {
        var input = Directory.CreateDirectory(Path.Combine(run, SandboxPaths.Input));
        var collected = Directory.CreateDirectory(Path.Combine(run, KubernetesJobSpec.Collected));
        foreach (var (file, content) in job.Input)
            await File.WriteAllBytesAsync(Path.Combine(input.FullName, file), content, ct)
                .ConfigureAwait(false);
        // The collector runs as the profile's user, not the launcher's; only its own run's
        // collected directory is mounted into it, and nothing of the sandbox's ever is.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(collected.FullName, (UnixFileMode)0b111_111_111);
    }

    /// <summary>
    /// Why no run may start or go on, when the runs volume's filesystem has less than
    /// <see cref="KubernetesBackendOptions.RunsMinFreePercent"/> free; null otherwise.
    /// </summary>
    private string? LowOnSpace()
    {
        var (available, total) = _space(Kube.RunsPath);
        return total > 0 && available * 100 >= total * Kube.RunsMinFreePercent
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"the runs volume has less than {Kube.RunsMinFreePercent}% free"
            );
    }

    /// <summary>The filesystem's own count of what is free under <paramref name="path"/>, which includes blocks held by deleted files still open.</summary>
    internal static (long Available, long Total) FreeSpace(string path)
    {
        var drive = new DriveInfo(path);
        return (drive.AvailableFreeSpace, drive.TotalSize);
    }

    /// <summary>
    /// Polls the Job until it ends, killing it when the runs volume falls below its free-space
    /// floor or the Job never starts. The run's own output volume needs no watching: its size is
    /// the storage's to enforce.
    /// </summary>
    private async Task<(SandboxEnd End, int? Code, string? Detail, string? Pod)> WatchAsync(
        string name,
        SandboxLimits limits,
        CancellationToken ct
    )
    {
        var giveUp = time.GetUtcNow() + TimeSpan.FromSeconds(limits.TimeoutSeconds) + StartGrace;
        while (true)
        {
            await Task.Delay(Kube.PollInterval, time, ct).ConfigureAwait(false);

            var job = await client
                .BatchV1.ReadNamespacedJobAsync(name, Kube.Namespace, cancellationToken: ct)
                .ConfigureAwait(false);
            if (Ended(job) is { } ended)
            {
                var (end, code, pod) = await EndOfAsync(name, ended, ct).ConfigureAwait(false);
                return (end, code, null, pod);
            }

            if (LowOnSpace() is { } low)
                return (SandboxEnd.Failed, null, low, null);

            if (time.GetUtcNow() > giveUp)
                return (SandboxEnd.Failed, null, "the Job did not start in time", null);
        }
    }

    /// <summary>Complete, Failed with DeadlineExceeded, or Failed otherwise; null while it runs.</summary>
    internal static string? Ended(V1Job job)
    {
        var condition = job.Status?.Conditions?.FirstOrDefault(c =>
            c.Type is "Complete" or "Failed"
            && string.Equals(c.Status, "True", StringComparison.Ordinal)
        );
        if (condition is null)
            return null;
        if (string.Equals(condition.Type, "Complete", StringComparison.Ordinal))
            return "Complete";
        return string.Equals(condition.Reason, "DeadlineExceeded", StringComparison.Ordinal)
            ? "DeadlineExceeded"
            : "Failed";
    }

    private async Task<(SandboxEnd, int?, string?)> EndOfAsync(
        string name,
        string ended,
        CancellationToken ct
    )
    {
        if (string.Equals(ended, "DeadlineExceeded", StringComparison.Ordinal))
            return (SandboxEnd.TimedOut, null, null);

        var pods = await client
            .CoreV1.ListNamespacedPodAsync(
                Kube.Namespace,
                labelSelector: $"job-name={name}",
                cancellationToken: ct
            )
            .ConfigureAwait(false);
        var (pod, terminated) = pods
            .Items.Select(p =>
                (p.Metadata?.Name, p.Status?.ContainerStatuses?.FirstOrDefault()?.State?.Terminated)
            )
            .FirstOrDefault(t => t.Terminated is not null);
        if (terminated is null)
            return (SandboxEnd.Exited, null, null);
        return (
            string.Equals(terminated.Reason, "OOMKilled", StringComparison.Ordinal)
                ? SandboxEnd.OutOfMemory
                : SandboxEnd.Exited,
            terminated.ExitCode,
            pod
        );
    }

    /// <summary>Deletes the Job and its pod, and waits until the pod is gone, so nothing writes the run's directory while it is removed.</summary>
    private async Task DeleteAndWaitAsync(string name, CancellationToken ct)
    {
        try
        {
            await client
                .BatchV1.DeleteNamespacedJobAsync(
                    name,
                    Kube.Namespace,
                    propagationPolicy: "Foreground",
                    gracePeriodSeconds: 0,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone.
        }

        var until = time.GetUtcNow() + TimeSpan.FromMinutes(2);
        while (time.GetUtcNow() < until)
        {
            var pods = await client
                .CoreV1.ListNamespacedPodAsync(
                    Kube.Namespace,
                    labelSelector: $"job-name={name}",
                    cancellationToken: ct
                )
                .ConfigureAwait(false);
            if (pods.Items.Count == 0)
                return;
            await Task.Delay(Kube.PollInterval, time, ct).ConfigureAwait(false);
        }
        throw new IOException(
            $"The pod of {name} is still there two minutes after its Job was deleted."
        );
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var jobs = await client
            .BatchV1.ListNamespacedJobAsync(
                Kube.Namespace,
                labelSelector: $"{KubernetesJobSpec.ManagedByLabel}={KubernetesJobSpec.ManagedBy}",
                cancellationToken: ct
            )
            .ConfigureAwait(false);
        foreach (var job in jobs.Items)
            await DeleteAndWaitAsync(job.Metadata.Name, ct).ConfigureAwait(false);
        // Only directories named as a run is: a misconfigured RunsPath must not cost anything else.
        foreach (var run in Directory.EnumerateDirectories(Kube.RunsPath))
            if (IsRunDirectory(Path.GetFileName(run)))
                try
                {
                    RemoveRun(run);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A directory left behind costs its space, not every later start.
                    logger.LogWarning(
                        ex,
                        "The sandbox run directory {Run} could not be removed; it is left in place.",
                        run
                    );
                }
    }

    /// <summary>Whether <paramref name="name"/> is a run id, 32 lowercase hex characters.</summary>
    internal static bool IsRunDirectory(string name) =>
        name.Length == 32 && name.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Runs <paramref name="step"/>, logging a failure to remove something; whether it succeeded.</summary>
    private async Task<bool> QuietlyAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
            when (ex
                    is HttpOperationException
                        or HttpRequestException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            logger.LogWarning(
                ex,
                "A sandbox resource could not be removed; the next start sweeps it."
            );
            return false;
        }
    }
}
