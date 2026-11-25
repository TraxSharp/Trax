using System.Net;
using AwesomeAssertions;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The migrate Job is waited on until it ends. A pod that can never start (its image cannot be pulled) ends the wait
/// at once instead of at the deadline, a container that has not started leaves a line in the log tail instead of an
/// error, and a Job given up on is deleted so it does not go on retrying. A pod no node can take ends the wait once it
/// has been unschedulable for two minutes, with the scheduler's reason.
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

    /// <summary>A pod the scheduler has not placed since <paramref name="since"/>, for want of CPU.</summary>
    internal static string Unscheduled(string name, string since) =>
        $$"""
            {
              "kind": "PodList", "apiVersion": "v1", "metadata": {},
              "items": [{
                "metadata": { "name": "{{name}}" },
                "status": {
                  "phase": "Pending",
                  "conditions": [{
                    "type": "PodScheduled", "status": "False", "reason": "Unschedulable",
                    "lastTransitionTime": "{{since}}",
                    "message": "0/1 nodes are available: 1 Insufficient cpu. preemption: 0/1 nodes are available."
                  }]
                }
              }]
            }
            """;

    private static StubKubernetesApi UnscheduledApi(string since) =>
        new StubKubernetesApi()
            .On("GET", $"{JobPath}/status", HttpStatusCode.OK, Running)
            .On("GET", Pods, HttpStatusCode.OK, Unscheduled($"{Job}-x", since))
            .On(
                "GET",
                $"{Pods}/{Job}-x/log",
                HttpStatusCode.BadRequest,
                StubKubernetesApi.Status(HttpStatusCode.BadRequest, "BadRequest", "pod is pending")
            )
            .On(
                "DELETE",
                JobPath,
                HttpStatusCode.OK,
                """{ "kind": "Status", "apiVersion": "v1", "status": "Success" }"""
            );

    [Test]
    public async Task A_pod_no_node_can_take_ends_the_wait_after_the_grace_with_the_schedulers_reason()
    {
        // Unschedulable from the moment the clock starts: the wait gives a cluster autoscaler two minutes.
        var api = UnscheduledApi("2026-11-17T12:00:00Z");
        var clock = new SteppingClock();

        var result = await api.Client(clock)
            .WaitForJobAsync(Ns, Job, KubernetesDeployer.MigrateTimeout, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result
            .Reason.Should()
            .Be(
                "its pod cannot be scheduled: 0/1 nodes are available: 1 Insufficient cpu. preemption: 0/1 nodes "
                    + "are available."
            );
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(KubernetesDeployClient.UnschedulableGrace);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(3));
        api.Requests.Should().Contain($"DELETE {JobPath}");
    }

    [Test]
    public async Task A_pod_unschedulable_for_longer_than_the_grace_ends_the_wait_at_once()
    {
        var api = UnscheduledApi("2026-11-17T11:50:00Z");
        var clock = new SteppingClock();

        var result = await api.Client(clock)
            .WaitForJobAsync(Ns, Job, KubernetesDeployer.MigrateTimeout, CancellationToken.None);

        result.Reason.Should().StartWith("its pod cannot be scheduled: 0/1 nodes are available");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}
