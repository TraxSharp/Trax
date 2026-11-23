using System.Net;
using AwesomeAssertions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy logs</c> on a kubernetes environment reads every pod of the service. A pod whose log the API will
/// not give (a container still waiting, no right to read logs) gets a line saying so and the others are still read;
/// a service with no pod is an error, not an empty success.
/// </summary>
public class KubernetesDeployClientLogsTests
{
    private const string Ns = "orders-staging";
    private const string Pods = $"/api/v1/namespaces/{Ns}/pods";

    private const string TwoPods = """
        {
          "kind": "PodList", "apiVersion": "v1", "metadata": {},
          "items": [
            { "metadata": { "name": "worker-a" }, "status": { "phase": "Running" } },
            {
              "metadata": { "name": "worker-b" },
              "status": {
                "phase": "Pending",
                "containerStatuses": [{
                  "name": "worker", "image": "orders-worker:1", "imageID": "", "ready": false, "restartCount": 0,
                  "state": { "waiting": { "reason": "ImagePullBackOff", "message": "Back-off pulling image" } }
                }]
              }
            }
          ]
        }
        """;

    private static async Task<List<string>> ReadAsync(StubKubernetesApi api)
    {
        var lines = new List<string>();
        await foreach (
            var line in api.Client(new SteppingClock())
                .LogsAsync(Ns, "orders", "worker", follow: false, 60, CancellationToken.None)
                .ConfigureAwait(false)
        )
            lines.Add(line);
        return lines;
    }

    [TestCase(HttpStatusCode.BadRequest, "BadRequest")]
    [TestCase(HttpStatusCode.Forbidden, "Forbidden")]
    public async Task A_pod_whose_log_is_refused_gets_a_line_and_the_others_are_read(
        HttpStatusCode status,
        string reason
    )
    {
        var api = new StubKubernetesApi()
            .On("GET", Pods, HttpStatusCode.OK, TwoPods)
            .On("GET", $"{Pods}/worker-a/log", HttpStatusCode.OK, "started\nready\n", "text/plain")
            .On(
                "GET",
                $"{Pods}/worker-b/log",
                status,
                StubKubernetesApi.Status(
                    status,
                    reason,
                    "container \"worker\" in pod \"worker-b\" is waiting to start"
                )
            );

        var lines = await ReadAsync(api);

        lines
            .Should()
            .BeEquivalentTo([
                "[worker-a] started",
                "[worker-a] ready",
                "[worker-b] no log (ImagePullBackOff: container \"worker\" in pod \"worker-b\" is waiting to start)",
            ]);
    }

    [Test]
    public async Task A_service_with_no_pod_is_refused()
    {
        var api = new StubKubernetesApi().On(
            "GET",
            Pods,
            HttpStatusCode.OK,
            """{ "kind": "PodList", "apiVersion": "v1", "metadata": {}, "items": [] }"""
        );

        var act = () => ReadAsync(api);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "No pod of 'worker' is in namespace orders-staging*"
        );
    }
}
