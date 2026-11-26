using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>
/// The Kubernetes API as far as the sandbox backend uses it, answering as RBAC would for the
/// launcher's Role: Jobs and pods only, no subresource, and the one StorageClass its ClusterRole
/// names. A sandbox Job reports running for <see cref="RunningPolls"/> reads and then
/// <see cref="Condition"/>; a collector Job plays the collector, writing <see cref="Output"/> as a
/// tar (or <see cref="CollectorRefuses"/>) into the run's collected directory, and reports
/// <see cref="CollectorCondition"/>. A delegating handler that never delegates, since the
/// Kubernetes client chains its own handler under whatever it is given.
/// </summary>
public sealed class FakeKubernetes(string runsPath) : DelegatingHandler
{
    private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);
    private int _polls;

    public IList<string> Calls { get; } = [];

    /// <summary>The sandbox Job as created.</summary>
    public JsonObject? CreatedJob { get; private set; }

    /// <summary>The collector Job as created.</summary>
    public JsonObject? CreatedCollector { get; private set; }

    /// <summary>What the sandbox left in its output volume, by path, for the collector to write.</summary>
    public IDictionary<string, string> Output { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>When set, the collector refuses the output with this reason.</summary>
    public string? CollectorRefuses { get; set; }

    /// <summary>The collector Job's condition.</summary>
    public (string Type, string? Reason) CollectorCondition { get; set; } = ("Complete", null);

    public int ExitCode { get; set; }

    public string? TerminatedReason { get; set; }

    /// <summary>The provisioner of the output StorageClass; null answers 404.</summary>
    public string? StorageProvisioner { get; set; } = "topolvm.io";

    /// <summary>The run's input directory as it was when the Job was created, by file name.</summary>
    public IDictionary<string, byte[]> InputAtCreate { get; } =
        new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>How many reads of the sandbox Job answer it as still running before it reports <see cref="Condition"/>.</summary>
    public int RunningPolls { get; set; }

    /// <summary>Called on each read of the sandbox Job with its count from 1, before the answer.</summary>
    public Func<int, Task>? OnPoll { get; set; }

    /// <summary>The sandbox Job's condition: Complete, or Failed with a reason such as DeadlineExceeded.</summary>
    public (string Type, string? Reason) Condition { get; set; } = ("Complete", null);

    /// <summary>When set, a deleted Job's pod is still listed forever, as a node that never tears it down.</summary>
    public bool PodsNeverGo { get; set; }

    /// <summary>Called on each list of pods, before the answer, with whether that Job was deleted yet.</summary>
    public Action<bool>? OnPodsList { get; set; }

    /// <summary>The pod name a Job's pod gets.</summary>
    public static string PodOf(string job) => $"{job}-x7k2p";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await AnswerAsync(request, cancellationToken).ConfigureAwait(false);
        response.RequestMessage = request;
        return response;
    }

    private async Task<HttpResponseMessage> AnswerAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
        lock (Calls)
            Calls.Add($"{request.Method} {request.RequestUri?.PathAndQuery}");

        // The launcher's Role grants no subresource; answer as the API server would.
        var resource = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (
            path.EndsWith("/status", StringComparison.Ordinal)
            || path.EndsWith("/log", StringComparison.Ordinal)
            || path.EndsWith("/exec", StringComparison.Ordinal)
        )
            return Json(HttpStatusCode.Forbidden, "{\"kind\":\"Status\",\"code\":403}");

        if (path.Contains("/storageclasses/", StringComparison.Ordinal))
            return StorageProvisioner is null
                ? Json(HttpStatusCode.NotFound, "{\"kind\":\"Status\",\"code\":404}")
                : Json(
                    HttpStatusCode.OK,
                    $"{{\"kind\":\"StorageClass\",\"apiVersion\":\"storage.k8s.io/v1\",\"metadata\":{{\"name\":\"{resource[^1]}\"}},\"provisioner\":\"{StorageProvisioner}\"}}"
                );

        if (request.Method == HttpMethod.Post && path.EndsWith("/jobs", StringComparison.Ordinal))
            return await CreateAsync(request, cancellationToken).ConfigureAwait(false);

        if (
            request.Method == HttpMethod.Get
            && string.Equals(resource[^2], "jobs", StringComparison.Ordinal)
        )
            return await ReadJobAsync(resource[^1]).ConfigureAwait(false);

        if (request.Method == HttpMethod.Delete)
        {
            lock (_deleted)
                _deleted.Add(resource[^1]);
            return Json(HttpStatusCode.OK, "{\"kind\":\"Status\",\"status\":\"Success\"}");
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/pods", StringComparison.Ordinal))
        {
            var job = query.Split("job-name=").LastOrDefault()?.Split('&')[0] ?? "";
            bool deleted;
            lock (_deleted)
                deleted = _deleted.Contains(job);
            OnPodsList?.Invoke(deleted);
            return Json(
                HttpStatusCode.OK,
                deleted && !PodsNeverGo ? "{\"items\":[]}" : PodsJson(job)
            );
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/jobs", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, "{\"items\":[]}");

        return Json(HttpStatusCode.NotFound, "{\"kind\":\"Status\",\"code\":404}");
    }

    private async Task<HttpResponseMessage> ReadJobAsync(string name)
    {
        if (name.EndsWith("-collect", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, JobJson(CollectorCondition));
        var poll = Interlocked.Increment(ref _polls);
        if (OnPoll is { } onPoll)
            await onPoll(poll).ConfigureAwait(false);
        return Json(HttpStatusCode.OK, poll <= RunningPolls ? RunningJson() : JobJson(Condition));
    }

    private async Task<HttpResponseMessage> CreateAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var body = await (
            request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult("{}")
        ).ConfigureAwait(false);
        var job = JsonNode.Parse(body)?.AsObject();
        var runId = job?["metadata"]?["labels"]?["trax.sandbox/run"]?.GetValue<string>() ?? "";
        if (
            job?["metadata"]?["name"]?.GetValue<string>() is { } name
            && name.EndsWith("-collect", StringComparison.Ordinal)
        )
        {
            CreatedCollector = job;
            await CollectAsync(Path.Combine(runsPath, runId, "collected"), ct)
                .ConfigureAwait(false);
            return Json(HttpStatusCode.Created, body);
        }

        CreatedJob = job;
        var input = Path.Combine(runsPath, runId, "in");
        if (Directory.Exists(input))
            foreach (var file in Directory.EnumerateFiles(input))
                InputAtCreate[Path.GetFileName(file)] = await File.ReadAllBytesAsync(file, ct)
                    .ConfigureAwait(false);
        return Json(HttpStatusCode.Created, body);
    }

    private async Task CollectAsync(string collected, CancellationToken ct)
    {
        Directory.CreateDirectory(collected);
        if (CollectorRefuses is { } refused)
        {
            await File.WriteAllTextAsync(Path.Combine(collected, "refused.txt"), refused, ct)
                .ConfigureAwait(false);
            return;
        }
        var tar = await SandboxArchive
            .WriteAsync(
                Output
                    .Select(o => new SandboxFile(o.Key, Encoding.UTF8.GetBytes(o.Value)))
                    .ToList(),
                ct
            )
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(collected, "output.tar"), tar, ct)
            .ConfigureAwait(false);
    }

    private static string JobJson((string Type, string? Reason) condition)
    {
        var reason = condition.Reason is null ? "" : $",\"reason\":\"{condition.Reason}\"";
        return $"{{\"kind\":\"Job\",\"apiVersion\":\"batch/v1\",\"metadata\":{{\"name\":\"x\"}},\"status\":{{\"conditions\":[{{\"type\":\"{condition.Type}\",\"status\":\"True\"{reason}}}]}}}}";
    }

    private static string RunningJson() =>
        "{\"kind\":\"Job\",\"apiVersion\":\"batch/v1\",\"metadata\":{\"name\":\"x\"},\"status\":{\"active\":1}}";

    private string PodsJson(string job)
    {
        var reason = TerminatedReason is null ? "" : $",\"reason\":\"{TerminatedReason}\"";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"items\":[{{\"metadata\":{{\"name\":\"{PodOf(job)}\"}},\"status\":{{\"containerStatuses\":[{{\"name\":\"sandbox\",\"image\":\"i\",\"imageID\":\"i\",\"ready\":false,\"restartCount\":0,\"state\":{{\"terminated\":{{\"exitCode\":{ExitCode}{reason}}}}}}}]}}}}]}}"
        );
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
