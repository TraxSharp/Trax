using System.Net;
using AwesomeAssertions;
using k8s.Models;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A Deployment or StatefulSet has rolled out only when the controller has seen its latest spec and every replica is
/// on it and up; one that does not within the timeout is an error naming what it reached. A missing namespace reads
/// as null, and a Secret's keys are read without its values.
/// </summary>
public class KubernetesDeployClientRolloutTests
{
    private const string Ns = "orders-staging";

    private static V1Deployment Deployment(
        long generation,
        long? observed,
        int updated,
        int available,
        int replicas
    ) =>
        new()
        {
            Metadata = new V1ObjectMeta { Name = "hub", Generation = generation },
            Spec = new V1DeploymentSpec { Replicas = 2 },
            Status = new V1DeploymentStatus
            {
                ObservedGeneration = observed,
                UpdatedReplicas = updated,
                AvailableReplicas = available,
                Replicas = replicas,
            },
        };

    [TestCase(2, 2, 2, 2, 2, true)]
    [TestCase(2, 1, 2, 2, 2, false)]
    [TestCase(2, null, 2, 2, 2, false)]
    [TestCase(2, 2, 1, 2, 2, false)]
    [TestCase(2, 2, 2, 1, 2, false)]
    [TestCase(2, 2, 2, 2, 3, false)]
    public void A_Deployment_is_rolled_out_when_every_replica_is_new_and_up(
        long generation,
        long? observed,
        int updated,
        int available,
        int replicas,
        bool done
    ) =>
        KubernetesDeployClient
            .Rolled(Deployment(generation, observed, updated, available, replicas))
            .Done.Should()
            .Be(done);

    [Test]
    public void A_Deployment_past_its_progress_deadline_says_so()
    {
        var d = Deployment(2, 2, 1, 1, 2);
        d.Status.Conditions =
        [
            new V1DeploymentCondition
            {
                Type = "Progressing",
                Status = "False",
                Reason = "ProgressDeadlineExceeded",
                Message = "ReplicaSet \"hub-7c\" has timed out progressing.",
            },
        ];

        var (done, state) = KubernetesDeployClient.Rolled(d);

        done.Should().BeFalse();
        state
            .Should()
            .StartWith(KubernetesDeployClient.PastDeadline)
            .And.Contain("timed out progressing");
    }

    [Test]
    public void A_deadline_from_an_earlier_spec_is_not_this_rollouts()
    {
        var d = Deployment(3, 2, 1, 1, 2);
        d.Status.Conditions =
        [
            new V1DeploymentCondition
            {
                Type = "Progressing",
                Status = "False",
                Reason = "ProgressDeadlineExceeded",
            },
        ];

        KubernetesDeployClient
            .Rolled(d)
            .State.Should()
            .NotStartWith(
                KubernetesDeployClient.PastDeadline,
                "the controller has not seen this deploy's spec yet"
            );
    }

    [TestCase(3, 3, 1, 1, true)]
    [TestCase(3, 2, 1, 1, false)]
    [TestCase(3, 3, 0, 1, false)]
    [TestCase(3, 3, 1, 0, false)]
    public void A_StatefulSet_is_rolled_out_when_its_replica_is_new_and_ready(
        long generation,
        long observed,
        int ready,
        int updated,
        bool done
    ) =>
        KubernetesDeployClient
            .Rolled(
                new V1StatefulSet
                {
                    Metadata = new V1ObjectMeta { Name = "database", Generation = generation },
                    Spec = new V1StatefulSetSpec { Replicas = 1 },
                    Status = new V1StatefulSetStatus
                    {
                        ObservedGeneration = observed,
                        ReadyReplicas = ready,
                        UpdatedReplicas = updated,
                    },
                }
            )
            .Done.Should()
            .Be(done);

    [Test]
    public async Task A_Deployment_that_does_not_roll_out_in_time_is_an_error_naming_its_state()
    {
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                HttpStatusCode.OK,
                """
                {
                  "kind": "Deployment", "apiVersion": "apps/v1",
                  "metadata": { "name": "hub", "generation": 2 },
                  "spec": { "replicas": 2, "selector": {}, "template": {} },
                  "status": { "observedGeneration": 2, "updatedReplicas": 1, "availableReplicas": 1, "replicas": 3 }
                }
                """
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                NoReplicaSets
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/pods", HttpStatusCode.OK, NoPods);
        var clock = new SteppingClock();

        var act = () =>
            api.Client(clock)
                .WaitForRolloutAsync(
                    Ns,
                    "Deployment",
                    "hub",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out within 10 minutes (1/2 available, 1 updated)*"
                + "'kubectl describe deployment hub -n orders-staging'*"
        );
        clock.Elapsed.Should().BeGreaterThan(KubernetesDeployer.RolloutTimeout);
    }

    private const string NoEvents = """{ "kind": "EventList", "apiVersion": "v1", "items": [] }""";

    private const string NoPods = """{ "kind": "PodList", "apiVersion": "v1", "items": [] }""";

    private const string NoReplicaSets =
        """{ "kind": "ReplicaSetList", "apiVersion": "apps/v1", "items": [] }""";

    private const string RollingHub = """
        {
          "kind": "Deployment", "apiVersion": "apps/v1",
          "metadata": { "name": "hub", "generation": 2 },
          "spec": { "replicas": 1, "selector": {}, "template": {} },
          "status": { "observedGeneration": 2, "updatedReplicas": 0, "availableReplicas": 0, "replicas": 1 }
        }
        """;

    private const string OnePod = """
        {
          "kind": "PodList", "apiVersion": "v1",
          "items": [ { "metadata": { "name": "hub-7c-abc" }, "status": { "containerStatuses": [
            { "name": "hub", "ready": false, "restartCount": 0, "image": "x", "imageID": "",
              "state": { "waiting": { "reason": "ContainerCreating" } } } ] } } ]
        }
        """;

    [Test]
    public async Task A_pod_the_node_cannot_give_a_user_namespace_ends_the_wait_at_once_saying_why()
    {
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                HttpStatusCode.OK,
                RollingHub
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                NoReplicaSets
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/pods", HttpStatusCode.OK, OnePod)
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/events",
                HttpStatusCode.OK,
                """
                {
                  "kind": "EventList", "apiVersion": "v1",
                  "items": [ { "metadata": { "name": "e1" }, "involvedObject": { "name": "hub-7c-abc" },
                    "reason": "FailedCreatePodSandBox", "count": 4,
                    "message": "runc create failed: error mounting \"sysfs\" to rootfs at \"/sys\": operation not permitted" } ]
                }
                """
            );
        var clock = new SteppingClock();

        var act = () =>
            api.Client(clock)
                .WaitForRolloutAsync(
                    Ns,
                    "Deployment",
                    "hub",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out: its pod cannot start (the node cannot give it a user namespace of its "
                + "own*hostUsers: false. kind cannot*"
        );
        clock
            .Elapsed.Should()
            .BeLessThan(TimeSpan.FromMinutes(1), "a pod that cannot start is not waited out");
    }

    [Test]
    public async Task A_pod_no_node_can_take_ends_the_rollout_after_the_grace_with_the_schedulers_reason()
    {
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                HttpStatusCode.OK,
                RollingHub
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                NoReplicaSets
            )
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/pods",
                HttpStatusCode.OK,
                KubernetesDeployClientJobTests.Unscheduled("hub-7c-abc", "2026-11-17T12:00:00Z")
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/events", HttpStatusCode.OK, NoEvents);
        var clock = new SteppingClock();

        var act = () =>
            api.Client(clock)
                .WaitForRolloutAsync(
                    Ns,
                    "Deployment",
                    "hub",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out: its pod cannot be scheduled: 0/1 nodes are available: 1 Insufficient "
                + "cpu.*'kubectl describe pods -l app.kubernetes.io/name=hub -n orders-staging'*"
        );
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(KubernetesDeployClient.UnschedulableGrace);
        clock
            .Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromMinutes(3),
                "a pod that will never fit is not waited out to the deadline"
            );
    }

    [Test]
    public void A_pod_within_the_grace_or_scheduled_is_not_reported()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        V1Pod Pod(string status, string reason, DateTimeOffset since) =>
            new()
            {
                Status = new V1PodStatus
                {
                    Conditions =
                    [
                        new V1PodCondition
                        {
                            Type = "PodScheduled",
                            Status = status,
                            Reason = reason,
                            LastTransitionTime = since.UtcDateTime,
                            Message = "0/1 nodes are available: 1 Insufficient memory.",
                        },
                    ],
                },
            };

        KubernetesDeployClient
            .Unschedulable([Pod("False", "Unschedulable", now.AddSeconds(-119))], now)
            .Should()
            .BeNull("an autoscaler gets two minutes to add a node");
        KubernetesDeployClient
            .Unschedulable([Pod("True", "", now.AddHours(-1))], now)
            .Should()
            .BeNull();
        KubernetesDeployClient
            .Unschedulable([Pod("False", "SchedulingGated", now.AddHours(-1))], now)
            .Should()
            .BeNull("a gated pod waits on purpose");
        KubernetesDeployClient
            .Unschedulable([Pod("False", "Unschedulable", now.AddMinutes(-2))], now)
            .Should()
            .Be("0/1 nodes are available: 1 Insufficient memory.");
        KubernetesDeployClient.Unschedulable([new V1Pod()], now).Should().BeNull();
    }

    [Test]
    public async Task A_pod_that_cannot_pull_its_image_ends_the_wait_at_once()
    {
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                HttpStatusCode.OK,
                RollingHub
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                NoReplicaSets
            )
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/pods",
                HttpStatusCode.OK,
                OnePod.Replace("ContainerCreating", "ImagePullBackOff", StringComparison.Ordinal)
            );

        var act = () =>
            api.Client(new SteppingClock())
                .WaitForRolloutAsync(
                    Ns,
                    "Deployment",
                    "hub",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out: its pod cannot start (ImagePullBackOff)*"
        );
    }

    [Test]
    public async Task A_rollback_is_not_failed_by_the_broken_pods_of_the_spec_it_abandoned()
    {
        // The new spec's pod cannot pull its image; the rollback reapplies the old spec (revision 3 now), and the
        // broken pod of revision 2 is still listed, not yet being deleted, when the wait first looks.
        var reads = 0;
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                () =>
                    StubKubernetesApi.Response(
                        HttpStatusCode.OK,
                        ++reads < 3
                            ? RollingHub
                                .Replace(
                                    "\"generation\": 2",
                                    "\"generation\": 3",
                                    StringComparison.Ordinal
                                )
                                .Replace(
                                    "\"observedGeneration\": 2",
                                    "\"observedGeneration\": 3",
                                    StringComparison.Ordinal
                                )
                            : RolledHub
                    )
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                TwoReplicaSets
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/pods", HttpStatusCode.OK, PodsOfBothSpecs)
            .On("GET", $"/api/v1/namespaces/{Ns}/events", HttpStatusCode.OK, NoEvents);

        await api.Client(new SteppingClock())
            .WaitForRolloutAsync(
                Ns,
                "Deployment",
                "hub",
                KubernetesDeployer.RolloutTimeout,
                CancellationToken.None
            );

        reads.Should().Be(3);
    }

    [Test]
    public async Task A_broken_pod_is_not_read_before_the_controller_has_seen_the_spec()
    {
        var reads = 0;
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                () =>
                    StubKubernetesApi.Response(
                        HttpStatusCode.OK,
                        ++reads < 2
                            ? RollingHub.Replace(
                                "\"generation\": 2",
                                "\"generation\": 3",
                                StringComparison.Ordinal
                            )
                            : RolledHub
                    )
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                NoReplicaSets
            )
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/pods",
                HttpStatusCode.OK,
                OnePod.Replace("ContainerCreating", "ImagePullBackOff", StringComparison.Ordinal)
            );

        await api.Client(new SteppingClock())
            .WaitForRolloutAsync(
                Ns,
                "Deployment",
                "hub",
                KubernetesDeployer.RolloutTimeout,
                CancellationToken.None
            );

        reads.Should().Be(2);
    }

    [Test]
    public async Task A_forward_roll_reports_the_broken_pod_of_its_newest_ReplicaSet_at_once()
    {
        // Revision 3 is this deploy's ReplicaSet; its pod cannot pull its image while revision 2's still runs.
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/deployments/hub/status",
                HttpStatusCode.OK,
                RollingHub
            )
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/replicasets",
                HttpStatusCode.OK,
                ForwardReplicaSets
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/pods", HttpStatusCode.OK, PodsOfBothSpecs)
            .On("GET", $"/api/v1/namespaces/{Ns}/events", HttpStatusCode.OK, NoEvents);
        var clock = new SteppingClock();

        var act = () =>
            api.Client(clock)
                .WaitForRolloutAsync(
                    Ns,
                    "Deployment",
                    "hub",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Deployment hub did not roll out: its pod cannot start (ImagePullBackOff)*"
        );
        clock
            .Elapsed.Should()
            .BeLessThan(TimeSpan.FromMinutes(1), "a pod that cannot start is not waited out");
    }

    private const string ForwardReplicaSets = """
        {
          "kind": "ReplicaSetList", "apiVersion": "apps/v1",
          "items": [
            { "metadata": { "name": "hub-old", "uid": "rs-old", "annotations": { "deployment.kubernetes.io/revision": "2" } } },
            { "metadata": { "name": "hub-new", "uid": "rs-new", "annotations": { "deployment.kubernetes.io/revision": "3" } } }
          ]
        }
        """;

    private const string RollingDatabase = """
        {
          "kind": "StatefulSet", "apiVersion": "apps/v1",
          "metadata": { "name": "database", "generation": 2 },
          "spec": { "replicas": 1, "selector": {}, "template": {}, "serviceName": "database" },
          "status": { "observedGeneration": 2, "replicas": 1, "readyReplicas": 0, "updatedReplicas": 0,
            "currentRevision": "database-old", "updateRevision": "database-new" }
        }
        """;

    private const string RolledDatabase = """
        {
          "kind": "StatefulSet", "apiVersion": "apps/v1",
          "metadata": { "name": "database", "generation": 2 },
          "spec": { "replicas": 1, "selector": {}, "template": {}, "serviceName": "database" },
          "status": { "observedGeneration": 2, "replicas": 1, "readyReplicas": 1, "updatedReplicas": 1,
            "currentRevision": "database-new", "updateRevision": "database-new" }
        }
        """;

    /// <summary>A pod of the StatefulSet at <paramref name="revision"/>, waiting for <paramref name="reason"/>.</summary>
    private static string DatabasePods(params (string Revision, string Reason)[] pods) =>
        """{ "kind": "PodList", "apiVersion": "v1", "items": [ """
        + string.Join(
            ", ",
            pods.Select(p =>
                $$"""
                    { "metadata": { "name": "database-0-{{p.Revision}}", "labels": { "controller-revision-hash": "{{p.Revision}}" } },
                      "status": { "containerStatuses": [ { "name": "database", "ready": false, "restartCount": 0, "image": "x",
                        "imageID": "", "state": { "waiting": { "reason": "{{p.Reason}}" } } } ] } }
                    """
            )
        )
        + "] }";

    [Test]
    public async Task A_StatefulSet_reports_the_broken_pod_of_its_update_revision_at_once()
    {
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/statefulsets/database/status",
                HttpStatusCode.OK,
                RollingDatabase
            )
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/pods",
                HttpStatusCode.OK,
                DatabasePods(("database-new", "ImagePullBackOff"))
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/events", HttpStatusCode.OK, NoEvents);
        var clock = new SteppingClock();

        var act = () =>
            api.Client(clock)
                .WaitForRolloutAsync(
                    Ns,
                    "StatefulSet",
                    "database",
                    KubernetesDeployer.RolloutTimeout,
                    CancellationToken.None
                );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "StatefulSet database did not roll out: its pod cannot start (ImagePullBackOff)*"
        );
        clock
            .Elapsed.Should()
            .BeLessThan(TimeSpan.FromMinutes(1), "a pod that cannot start is not waited out");
    }

    [Test]
    public async Task A_StatefulSet_is_not_failed_by_a_broken_pod_of_an_older_revision()
    {
        var reads = 0;
        var api = new StubKubernetesApi()
            .On(
                "GET",
                $"/apis/apps/v1/namespaces/{Ns}/statefulsets/database/status",
                () =>
                    StubKubernetesApi.Response(
                        HttpStatusCode.OK,
                        ++reads < 3 ? RollingDatabase : RolledDatabase
                    )
            )
            .On(
                "GET",
                $"/api/v1/namespaces/{Ns}/pods",
                HttpStatusCode.OK,
                DatabasePods(
                    ("database-old", "ImagePullBackOff"),
                    ("database-new", "ContainerCreating")
                )
            )
            .On("GET", $"/api/v1/namespaces/{Ns}/events", HttpStatusCode.OK, NoEvents);

        await api.Client(new SteppingClock())
            .WaitForRolloutAsync(
                Ns,
                "StatefulSet",
                "database",
                KubernetesDeployer.RolloutTimeout,
                CancellationToken.None
            );

        reads.Should().Be(3);
    }

    private const string TwoReplicaSets = """
        {
          "kind": "ReplicaSetList", "apiVersion": "apps/v1",
          "items": [
            { "metadata": { "name": "hub-new", "uid": "rs-new", "annotations": { "deployment.kubernetes.io/revision": "2" } } },
            { "metadata": { "name": "hub-old", "uid": "rs-old", "annotations": { "deployment.kubernetes.io/revision": "3" } } }
          ]
        }
        """;

    private const string PodsOfBothSpecs = """
        {
          "kind": "PodList", "apiVersion": "v1",
          "items": [
            { "metadata": { "name": "hub-new-abc", "ownerReferences": [ { "apiVersion": "apps/v1", "kind": "ReplicaSet", "name": "hub-new", "uid": "rs-new" } ] },
              "status": { "containerStatuses": [ { "name": "hub", "ready": false, "restartCount": 0, "image": "x", "imageID": "",
                "state": { "waiting": { "reason": "ImagePullBackOff" } } } ] } },
            { "metadata": { "name": "hub-terminating", "deletionTimestamp": "2026-11-25T12:00:00Z" },
              "status": { "containerStatuses": [ { "name": "hub", "ready": false, "restartCount": 0, "image": "x", "imageID": "",
                "state": { "waiting": { "reason": "ErrImagePull" } } } ] } },
            { "metadata": { "name": "hub-old-def", "ownerReferences": [ { "apiVersion": "apps/v1", "kind": "ReplicaSet", "name": "hub-old", "uid": "rs-old" } ] },
              "status": { "containerStatuses": [ { "name": "hub", "ready": true, "restartCount": 0, "image": "x", "imageID": "",
                "state": { "running": {} } } ] } }
          ]
        }
        """;

    private const string RolledHub = """
        {
          "kind": "Deployment", "apiVersion": "apps/v1",
          "metadata": { "name": "hub", "generation": 3 },
          "spec": { "replicas": 1, "selector": {}, "template": {} },
          "status": { "observedGeneration": 3, "updatedReplicas": 1, "availableReplicas": 1, "replicas": 1 }
        }
        """;

    [TestCase(1, 0, false)]
    [TestCase(2, 0, false)]
    [TestCase(3, 0, true)]
    [TestCase(1, 5, true)]
    public void A_sandbox_counts_as_failed_after_three_attempts(int count, int series, bool failed)
    {
        var events = new[]
        {
            new Corev1Event
            {
                Reason = KubernetesDeployClient.SandboxFailed,
                Count = count,
                Series = series == 0 ? null : new Corev1EventSeries { Count = series },
                Message = "failed to create containerd task",
                InvolvedObject = new V1ObjectReference { Name = "hub-1" },
                Metadata = new V1ObjectMeta { Name = "e" },
            },
        };

        (KubernetesDeployClient.SandboxFailure(events) is not null).Should().Be(failed);
    }

    [Test]
    public void A_sandbox_failure_that_is_not_about_user_namespaces_is_reported_as_it_is()
    {
        var events = new[]
        {
            new Corev1Event
            {
                Reason = KubernetesDeployClient.SandboxFailed,
                Count = 3,
                Message = "failed to set up network: no IP addresses available",
                InvolvedObject = new V1ObjectReference { Name = "hub-1" },
                Metadata = new V1ObjectMeta { Name = "e" },
            },
        };

        KubernetesDeployClient
            .SandboxFailure(events)
            .Should()
            .Be(
                "the node failed 3 times to create its sandbox: failed to set up network: no IP addresses available"
            );
    }

    [Test]
    public async Task A_missing_namespace_reads_as_null_and_a_present_one_as_its_labels()
    {
        var api = new StubKubernetesApi().On(
            "GET",
            "/api/v1/namespaces/billing-staging",
            HttpStatusCode.OK,
            """
            { "kind": "Namespace", "apiVersion": "v1", "metadata": { "name": "billing-staging", "labels": { "trax/app": "billing" } } }
            """
        );
        var client = api.Client(new SteppingClock());

        (await client.NamespaceLabelsAsync(Ns, CancellationToken.None)).Should().BeNull();
        (await client.NamespaceLabelsAsync("billing-staging", CancellationToken.None))
            .Should()
            .Contain(KubernetesTarget.AppLabel, "billing");
    }

    [Test]
    public async Task A_secret_is_read_as_its_key_names_only()
    {
        var api = new StubKubernetesApi().On(
            "GET",
            $"/api/v1/namespaces/{Ns}/secrets/orders-staging",
            HttpStatusCode.OK,
            """
            {
              "kind": "Secret", "apiVersion": "v1", "metadata": { "name": "orders-staging" },
              "data": { "POSTGRES_PASSWORD": "aHVudGVyMg==", "Stripe__ApiKey": "c2tfdGVzdA==" }
            }
            """
        );

        var client = api.Client(new SteppingClock());

        (await client.SecretKeysAsync(Ns, "orders-staging", CancellationToken.None))
            .Should()
            .BeEquivalentTo(["POSTGRES_PASSWORD", "Stripe__ApiKey"]);
        (await client.SecretKeysAsync(Ns, "none", CancellationToken.None)).Should().BeNull();
    }
}
