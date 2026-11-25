using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>
/// The Kubernetes API as far as the sandbox backend uses it, answering as RBAC would for the
/// launcher's Role: Jobs and pods only, no subresource. When a Job is created it plays the
/// sandbox, writing <see cref="Output"/> into the run's output directory, then reports the Job
/// complete with <see cref="ExitCode"/>. A delegating handler that never delegates, since the
/// Kubernetes client chains its own handler under whatever it is given.
/// </summary>
public sealed class FakeKubernetes(string runsPath) : DelegatingHandler
{
    private bool _deleted;

    public IList<string> Calls { get; } = [];

    public JsonObject? CreatedJob { get; private set; }

    public IDictionary<string, string> Output { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int ExitCode { get; set; }

    public string? TerminatedReason { get; set; }

    /// <summary>The run's input directory as it was when the Job was created, by file name.</summary>
    public IDictionary<string, byte[]> InputAtCreate { get; } =
        new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>The Job's condition: Complete, or Failed with a reason such as DeadlineExceeded.</summary>
    public (string Type, string? Reason) Condition { get; set; } = ("Complete", null);

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

        if (request.Method == HttpMethod.Post && path.EndsWith("/jobs", StringComparison.Ordinal))
            return await CreateAsync(request, cancellationToken).ConfigureAwait(false);

        if (
            request.Method == HttpMethod.Get
            && string.Equals(resource[^2], "jobs", StringComparison.Ordinal)
        )
            return Json(HttpStatusCode.OK, JobJson());

        if (request.Method == HttpMethod.Delete)
        {
            _deleted = true;
            return Json(HttpStatusCode.OK, "{\"kind\":\"Status\",\"status\":\"Success\"}");
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/pods", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, _deleted ? "{\"items\":[]}" : PodsJson());

        if (request.Method == HttpMethod.Get && path.EndsWith("/jobs", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, "{\"items\":[]}");

        return Json(HttpStatusCode.NotFound, "{\"kind\":\"Status\",\"code\":404}");
    }

    private async Task<HttpResponseMessage> CreateAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var body = await (
            request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult("{}")
        ).ConfigureAwait(false);
        CreatedJob = JsonNode.Parse(body)?.AsObject();
        var runId =
            CreatedJob?["metadata"]?["labels"]?["trax.sandbox/run"]?.GetValue<string>() ?? "";
        var input = Path.Combine(runsPath, runId, "in");
        if (Directory.Exists(input))
            foreach (var file in Directory.EnumerateFiles(input))
                InputAtCreate[Path.GetFileName(file)] = await File.ReadAllBytesAsync(file, ct)
                    .ConfigureAwait(false);
        var output = Path.Combine(runsPath, runId, "out");
        foreach (var (name, content) in Output)
        {
            var file = Path.Combine(output, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? output);
            await File.WriteAllTextAsync(file, content, ct).ConfigureAwait(false);
        }
        return Json(HttpStatusCode.Created, body);
    }

    private string JobJson()
    {
        var reason = Condition.Reason is null ? "" : $",\"reason\":\"{Condition.Reason}\"";
        return $"{{\"kind\":\"Job\",\"apiVersion\":\"batch/v1\",\"metadata\":{{\"name\":\"x\"}},\"status\":{{\"conditions\":[{{\"type\":\"{Condition.Type}\",\"status\":\"True\"{reason}}}]}}}}";
    }

    private string PodsJson()
    {
        var reason = TerminatedReason is null ? "" : $",\"reason\":\"{TerminatedReason}\"";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"items\":[{{\"metadata\":{{\"name\":\"p\"}},\"status\":{{\"containerStatuses\":[{{\"name\":\"sandbox\",\"image\":\"i\",\"imageID\":\"i\",\"ready\":false,\"restartCount\":0,\"state\":{{\"terminated\":{{\"exitCode\":{ExitCode}{reason}}}}}}}]}}}}]}}"
        );
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
