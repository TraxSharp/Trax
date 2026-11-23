using System.Net;
using System.Text;
using System.Text.Json;
using k8s;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Answers for a Kubernetes API server: each route is a method and a path, and anything not routed is a 404. The
/// requests made are kept in order, so a test can say what the client asked for.
/// </summary>
internal sealed class StubKubernetesApi : DelegatingHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(
        StringComparer.Ordinal
    );

    public List<string> Requests { get; } = [];

    public StubKubernetesApi On(
        string method,
        string path,
        HttpStatusCode status,
        string body,
        string mediaType = "application/json"
    ) => On(method, path, () => Response(status, body, mediaType));

    public StubKubernetesApi On(string method, string path, Func<HttpResponseMessage> answer)
    {
        _routes[$"{method} {path}"] = answer;
        return this;
    }

    public KubernetesDeployClient Client(TimeProvider clock) =>
        new(new KubernetesClientConfiguration { Host = "http://cluster.test" }, clock, this);

    /// <summary>The body the API server sends with a refusal.</summary>
    public static string Status(HttpStatusCode code, string reason, string message) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "Status",
                ["apiVersion"] = "v1",
                ["status"] = "Failure",
                ["message"] = message,
                ["reason"] = reason,
                ["code"] = (int)code,
            }
        );

    public static HttpResponseMessage Response(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json"
    ) => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var key = $"{request.Method} {request.RequestUri.NotNull().AbsolutePath}";
        lock (Requests)
            Requests.Add(key);
        var response = _routes.TryGetValue(key, out var answer)
            ? answer()
            : Response(
                HttpStatusCode.NotFound,
                Status(HttpStatusCode.NotFound, "NotFound", $"{key} not found")
            );
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
