using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Podman's API as far as the launcher uses it, recording every call.</summary>
public sealed class FakePodman : HttpMessageHandler
{
    public IList<string> Calls { get; } = [];

    public IDictionary<string, JsonObject> Created { get; } =
        new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    public byte[] Output { get; set; } = [];

    public bool OomKilled { get; set; }

    public int ExitCode { get; set; }

    /// <summary>The first wait on the sandbox never returns; it ends only when cancelled.</summary>
    public bool Hang { get; set; }

    /// <summary>Creating the sandbox (not the holder) fails like a missing runtime.</summary>
    public bool RuntimeMissing { get; set; }

    public byte[]? PutArchive { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        var call = $"{request.Method} {path}";
        lock (Calls)
            Calls.Add(call);

        if (path.EndsWith("/containers/create", StringComparison.Ordinal))
            return await CreateAsync(request, cancellationToken).ConfigureAwait(false);

        if (path.Contains("/wait", StringComparison.Ordinal))
        {
            if (Hang)
            {
                Hang = false;
                // determinism: a wait that ends only when cancelled, as podman's does for a hung container.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            return Json(
                HttpStatusCode.OK,
                ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }

        if (request.Method == HttpMethod.Put)
        {
            PutArchive = await (
                request.Content?.ReadAsByteArrayAsync(cancellationToken)
                ?? Task.FromResult(Array.Empty<byte>())
            ).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (request.Method == HttpMethod.Get && path.Contains("/archive", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Output),
            };

        if (request.Method == HttpMethod.Get && path.EndsWith("/json", StringComparison.Ordinal))
            return Json(
                HttpStatusCode.OK,
                $"{{\"State\":{{\"OOMKilled\":{(OomKilled ? "true" : "false")}}}}}"
            );

        if (request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, "[]");

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private async Task<HttpResponseMessage> CreateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var spec =
            JsonNode
                .Parse(
                    await (
                        request.Content?.ReadAsStringAsync(cancellationToken)
                        ?? Task.FromResult("{}")
                    ).ConfigureAwait(false)
                )
                ?.AsObject()
            ?? [];
        var name = spec["name"]?.GetValue<string>() ?? "";
        if (RuntimeMissing && !name.EndsWith("-holder", StringComparison.Ordinal))
            return Json(
                HttpStatusCode.InternalServerError,
                "{\"message\":\"default OCI runtime \\\"kata\\\" not found\"}"
            );
        Created[name] = spec;
        return Json(HttpStatusCode.Created, $"{{\"Id\":\"{name}\"}}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
