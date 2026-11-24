using AwesomeAssertions;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

public partial class DeployEgressTests
{
    private static (
        KubernetesDeployer Deployer,
        KubernetesDeployerTests.FakeCluster Cluster
    ) Deployer()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        return (
            new KubernetesDeployer(cluster, new KubernetesDeployerTests.FakeImages(), _ => { }),
            cluster
        );
    }

    private static Task Deploy(KubernetesDeployer deployer) =>
        deployer.DeployAsync(
            Held(),
            "/src/trax.deploy.json",
            "staging",
            DeployKubernetesTests.Exports(),
            Tag,
            CancellationToken.None
        );

    [Test]
    public async Task The_deployer_applies_the_policy_before_the_database_the_migration_or_any_service()
    {
        var (deployer, cluster) = Deployer();

        await Deploy(deployer);

        var applies = cluster
            .Calls.Where(c =>
                c.StartsWith("apply", StringComparison.Ordinal)
                || c.StartsWith("create-job", StringComparison.Ordinal)
            )
            .ToList();
        applies
            .Should()
            .StartWith([
                "apply ValidatingAdmissionPolicy/trax-egress-orders-staging",
                "apply ValidatingAdmissionPolicyBinding/trax-egress-orders-staging",
            ]);
        applies
            .FindIndex(c => c.StartsWith("apply NetworkPolicy/", StringComparison.Ordinal))
            .Should()
            .Be(2, "the network policies come next, before anything they protect");
        applies
            .IndexOf("apply Service/database")
            .Should()
            .BeGreaterThan(
                applies.FindLastIndex(c =>
                    c.StartsWith("apply NetworkPolicy/", StringComparison.Ordinal)
                )
            );
    }

    [Test]
    public async Task The_deployer_refuses_a_policy_another_app_wrote_before_it_applies_anything()
    {
        var (deployer, cluster) = Deployer();
        cluster.ClusterObjects["ValidatingAdmissionPolicy/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicy",
                "trax-egress-orders-staging",
                "billing"
            );

        await FluentActions
            .Awaiting(() => Deploy(deployer))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "ValidatingAdmissionPolicy trax-egress-orders-staging belongs to app 'billing'*"
            );
        cluster.Calls.Should().NotContain(c => c.StartsWith("apply", StringComparison.Ordinal));
    }

    [Test]
    public async Task The_deployer_refuses_a_policy_trax_did_not_write()
    {
        var (deployer, cluster) = Deployer();
        cluster.ClusterObjects["ValidatingAdmissionPolicyBinding/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicyBinding",
                "trax-egress-orders-staging",
                App: null
            );

        await FluentActions
            .Awaiting(() => Deploy(deployer))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no trax/app label, so trax deploy did not create it*");
    }

    [Test]
    public async Task The_deployer_takes_back_its_own_policy()
    {
        var (deployer, cluster) = Deployer();
        cluster.ClusterObjects["ValidatingAdmissionPolicy/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicy",
                "trax-egress-orders-staging",
                "orders"
            );

        await Deploy(deployer);

        cluster
            .Calls.Should()
            .Contain("apply ValidatingAdmissionPolicy/trax-egress-orders-staging");
    }

    [Test]
    public async Task Destroy_deletes_the_binding_then_the_policy_it_wrote_and_nothing_of_another_app()
    {
        var (deployer, cluster) = Deployer();
        cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesTarget.AppLabel] = "orders",
        };
        cluster.ClusterObjects["ValidatingAdmissionPolicy/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicy",
                "trax-egress-orders-staging",
                "orders"
            );
        cluster.ClusterObjects["ValidatingAdmissionPolicyBinding/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicyBinding",
                "trax-egress-orders-staging",
                "orders"
            );

        await deployer.DestroyAsync(Held(), "staging", deleteData: false, CancellationToken.None);

        cluster
            .Calls.Where(c => c.Contains("ValidatingAdmissionPolicy", StringComparison.Ordinal))
            .Should()
            .Equal(
                "delete ValidatingAdmissionPolicyBinding/trax-egress-orders-staging",
                "delete ValidatingAdmissionPolicy/trax-egress-orders-staging"
            );

        var (other, otherCluster) = Deployer();
        otherCluster.Namespace = cluster.Namespace;
        otherCluster.ClusterObjects["ValidatingAdmissionPolicy/trax-egress-orders-staging"] =
            new KubernetesObjectState(
                "ValidatingAdmissionPolicy",
                "trax-egress-orders-staging",
                "billing"
            );
        await other.DestroyAsync(Held(), "staging", deleteData: false, CancellationToken.None);
        otherCluster
            .Calls.Should()
            .NotContain(c => c.Contains("ValidatingAdmissionPolicy", StringComparison.Ordinal));
    }
}
