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
        var api = new StubKubernetesApi().On(
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
            "Deployment hub did not roll out within 10 minutes (1/2 available, 1 updated)*"
                + "'kubectl describe deployment hub -n orders-staging'*"
        );
        clock.Elapsed.Should().BeGreaterThan(KubernetesDeployer.RolloutTimeout);
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
