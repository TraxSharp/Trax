using System.Globalization;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// Runs a sandbox as a Job in the dedicated namespace, through a ServiceAccount that may create,
/// read and delete Jobs and read pods there, and nothing else: no <c>pods/exec</c>, no
/// <c>pods/log</c>.
/// </summary>
/// <remarks>
/// Input and output go through a volume the launcher's own pod mounts: each run gets a directory
/// on it, whose input directory the sandbox mounts read-only and whose output directory it mounts writable,
/// each by subPath, so it sees nothing of other runs. The launcher writes the input before
/// creating the Job, and reads the output only after the pod is gone. The output volume has no
/// size of its own here, so the launcher checks its size while the sandbox runs and kills a run
/// that goes over; the volume's own size is the hard limit behind that. See scheduler/0021.
/// </remarks>
internal sealed class KubernetesSandboxBackend(
    IKubernetes client,
    SandboxLauncherOptions options,
    TimeProvider time,
    ILogger<KubernetesSandboxBackend> logger
) : ISandboxBackend
{
    private const long MiB = 1024 * 1024;

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

    public async Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct)
    {
        var run = Path.Combine(Kube.RunsPath, job.RunId);
        var name = KubernetesJobSpec.JobName(job.RunId);
        try
        {
            await PrepareAsync(run, job, ct).ConfigureAwait(false);
            await client
                .BatchV1.CreateNamespacedJobAsync(
                    KubernetesJobSpec.Build(job, Kube),
                    Kube.Namespace,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);

            var (end, code) = await WatchAsync(name, run, job.Limits, ct).ConfigureAwait(false);
            await DeleteAndWaitAsync(name, ct).ConfigureAwait(false);
            if (end is SandboxEnd.Failed or SandboxEnd.DiskExceeded)
                return new SandboxExecution(
                    end,
                    code,
                    Output: [],
                    OutputRefused: null,
                    Detail: end == SandboxEnd.Failed ? "the Job did not start in time" : null
                );

            var (files, refused) = await DirectoryOutput
                .ReadAsync(
                    Path.Combine(run, SandboxPaths.Output),
                    PodmanSandboxBackend.OutputLimits(options.Caps),
                    ct
                )
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
            await CleanUpAsync(name, run).ConfigureAwait(false);
        }
    }

    /// <summary>Removes the run's Job and directory, on an uncancellable token: a run whose caller gave up must not leave either behind.</summary>
    private async Task CleanUpAsync(string name, string run)
    {
        await QuietlyAsync(() => DeleteAndWaitAsync(name, CancellationToken.None))
            .ConfigureAwait(false);
        await QuietlyAsync(() =>
            {
                if (Directory.Exists(run))
                    Directory.Delete(run, recursive: true);
                return Task.CompletedTask;
            })
            .ConfigureAwait(false);
    }

    private static async Task PrepareAsync(string run, SandboxJob job, CancellationToken ct)
    {
        var input = Directory.CreateDirectory(Path.Combine(run, SandboxPaths.Input));
        var output = Directory.CreateDirectory(Path.Combine(run, SandboxPaths.Output));
        foreach (var (file, content) in job.Input)
            await File.WriteAllBytesAsync(Path.Combine(input.FullName, file), content, ct)
                .ConfigureAwait(false);
        // The sandbox runs as its profile's user, not the launcher's; only its own run's output
        // directory is mounted into it.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(output.FullName, (UnixFileMode)0b111_111_111);
    }

    /// <summary>Polls the Job until it ends, killing it when its output grows past its disk limit.</summary>
    private async Task<(SandboxEnd, int?)> WatchAsync(
        string name,
        string run,
        SandboxLimits limits,
        CancellationToken ct
    )
    {
        var giveUp = time.GetUtcNow() + TimeSpan.FromSeconds(limits.TimeoutSeconds) + StartGrace;
        var output = Path.Combine(run, SandboxPaths.Output);
        while (true)
        {
            await Task.Delay(Kube.PollInterval, time, ct).ConfigureAwait(false);

            var job = await client
                .BatchV1.ReadNamespacedJobStatusAsync(name, Kube.Namespace, cancellationToken: ct)
                .ConfigureAwait(false);
            if (Ended(job) is { } ended)
                return await EndOfAsync(name, ended, ct).ConfigureAwait(false);

            if (
                DirectoryOutput.Size(output, options.Caps.MaxArchiveFiles + 2)
                > limits.DiskMiB * MiB
            )
                return (SandboxEnd.DiskExceeded, null);

            if (time.GetUtcNow() > giveUp)
                return (SandboxEnd.Failed, null);
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

    private async Task<(SandboxEnd, int?)> EndOfAsync(
        string name,
        string ended,
        CancellationToken ct
    )
    {
        if (string.Equals(ended, "DeadlineExceeded", StringComparison.Ordinal))
            return (SandboxEnd.TimedOut, null);

        var pods = await client
            .CoreV1.ListNamespacedPodAsync(
                Kube.Namespace,
                labelSelector: $"job-name={name}",
                cancellationToken: ct
            )
            .ConfigureAwait(false);
        var terminated = pods
            .Items.Select(p => p.Status?.ContainerStatuses?.FirstOrDefault()?.State?.Terminated)
            .FirstOrDefault(t => t is not null);
        if (terminated is null)
            return (SandboxEnd.Exited, null);
        return (
            string.Equals(terminated.Reason, "OOMKilled", StringComparison.Ordinal)
                ? SandboxEnd.OutOfMemory
                : SandboxEnd.Exited,
            terminated.ExitCode
        );
    }

    /// <summary>Deletes the Job and its pod, and waits until the pod is gone, so nothing writes the output while it is read.</summary>
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
                Directory.Delete(run, recursive: true);
    }

    /// <summary>Whether <paramref name="name"/> is a run id, 32 lowercase hex characters.</summary>
    internal static bool IsRunDirectory(string name) =>
        name.Length == 32 && name.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    private async Task QuietlyAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
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
        }
    }
}
