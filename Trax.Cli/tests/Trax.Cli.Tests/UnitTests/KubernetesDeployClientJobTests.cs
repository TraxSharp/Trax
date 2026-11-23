using System.Net;
using AwesomeAssertions;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The migrate Job is waited on until it ends. A pod that can never start (its image cannot be pulled) ends the wait
/// at once instead of at the deadline, a container that has not started leaves a line in the log tail instead of an
/// error, and a Job given up on is deleted so it does not go on retrying.
/// </summary>
public class KubernetesDeployClientJobTests
{
    private const string Ns = "orders-staging";
    private const string Job = "orders-migrate-1";
    private const string JobPath = $"/apis/batch/v1/namespaces/{Ns}/jobs/{Job}";
    private const string Pods = $"/api/v1/namespaces/{Ns}/pods";

    private const string Running = $$"""
        { "kind": "Job", "apiVersion": "batch/v1", "metadata": { "name": "{{Job}}" }, "status": { "active": 1 } }
        """;

    private static string Pod(string reason) =>
        $$"""
            {
              "kind": "PodList", "apiVersion": "v1", "metadata": {},
              "items": [{
                "metadata": { "name": "{{Job}}-x" },
                "status": {
                  "phase": "Pending",
                  "containerStatuses": [{
                    "name": "migrate", "image": "orders-hub:1", "imageID": "", "ready": false, "restartCount": 0,
                    "state": { "waiting": { "reason": "{{reason}}", "message": "the image is not there" } }
                  }]
                }
              }]
            }
            """;

    private static StubKubernetesApi Api(string podReason) =>
        new StubKubernetesApi()
            .On("GET", $"{JobPath}/status", HttpStatusCode.OK, Running)
            .On("GET", Pods, HttpStatusCode.OK, Pod(podReason))
            .On(
                "GET",
                $"{Pods}/{Job}-x/log",
                HttpStatusCode.BadRequest,
                StubKubernetesApi.Status(
                    HttpStatusCode.BadRequest,
                    "BadRequest",
                    "container \"migrate\" in pod is waiting to start"
                )
            )
            .On(
                "DELETE",
                JobPath,
                HttpStatusCode.OK,
                """{ "kind": "Status", "apiVersion": "v1", "status": "Success" }"""
            );

    [TestCase("ImagePullBackOff")]
    [TestCase("ErrImagePull")]
    [TestCase("CreateContainerConfigError")]
    [TestCase("InvalidImageName")]
    public async Task A_pod_that_cannot_start_ends_the_wait_and_the_job(string reason)
    {
        var api = Api(reason);
        var clock = new SteppingClock();

        var result = await api.Client(clock)
            .WaitForJobAsync(Ns, Job, KubernetesDeployer.MigrateTimeout, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be($"{reason}: the image is not there");
        result.LogTail.Should().Be($"[{Job}-x] no log ({reason}: the image is not there)");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1));
        api.Requests.Should().Contain($"DELETE {JobPath}");
    }

    [Test]
    public async Task A_job_still_running_at_the_deadline_is_deleted()
    {
        var api = Api("ContainerCreating");
        var clock = new SteppingClock();

        var result = await api.Client(clock)
            .WaitForJobAsync(Ns, Job, KubernetesDeployer.MigrateTimeout, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be("still running after 30 minutes");
        result.LogTail.Should().Contain("no log (ContainerCreating");
        clock.Elapsed.Should().BeGreaterThan(KubernetesDeployer.MigrateTimeout);
        api.Requests.Should().Contain($"DELETE {JobPath}");
    }
}
