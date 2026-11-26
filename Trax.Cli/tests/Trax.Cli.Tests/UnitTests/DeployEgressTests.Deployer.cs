using AwesomeAssertions;
using k8s.Models;
using Trax.Cli.Deploy;
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

    private const string PreviousImage =
        "ghcr.io/traxsharp/trax-egress@sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    /// <summary>The hub as an earlier deploy left it: held by the egress image <paramref name="image"/>.</summary>
    private static V1Deployment HeldBefore(string image) =>
        new()
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new V1ObjectMeta
            {
                Name = "hub",
                NamespaceProperty = "orders-staging",
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesTarget.AppLabel] = "orders",
                },
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        InitContainers =
                        [
                            new V1Container { Name = DeployEgress.RulesContainer, Image = image },
                            new V1Container { Name = DeployEgress.SidecarContainer, Image = image },
                        ],
                        Containers = [new V1Container { Name = "hub", Image = "orders-hub:old" }],
                    },
                },
            },
        };

    private static string EgressImages(V1ValidatingAdmissionPolicy policy) =>
        policy
            .Spec.Variables.Single(v =>
                string.Equals(v.Name, "egressImages", StringComparison.Ordinal)
            )
            .Expression;

    [Test]
    public async Task The_deployer_allows_the_previous_egress_image_while_the_services_roll_then_narrows()
    {
        var (deployer, cluster) = Deployer();
        cluster.Deployments["hub"] = HeldBefore(PreviousImage);

        await Deploy(deployer);

        var policies = cluster.Applied.OfType<V1ValidatingAdmissionPolicy>().ToList();
        policies.Should().HaveCount(2);
        EgressImages(policies[0])
            .Should()
            .Contain(Image)
            .And.Contain(PreviousImage, "the old pods are still created while the roll runs");
        EgressImages(policies[1]).Should().Be($"['{Image}']");
        var calls = cluster.Calls;
        calls
            .LastIndexOf("apply ValidatingAdmissionPolicy/trax-egress-orders-staging")
            .Should()
            .BeGreaterThan(calls.LastIndexOf("wait Deployment/hub"));
    }

    [Test]
    public async Task A_narrowing_the_cluster_refuses_still_ends_the_drain()
    {
        var (deployer, cluster) = Deployer();
        cluster.Deployments["hub"] = HeldBefore(PreviousImage);
        var policies = 0;
        cluster.OnApply = o =>
        {
            if (o is V1ValidatingAdmissionPolicy && ++policies == 2)
                throw new HttpRequestException("the connection was reset");
        };

        await FluentActions
            .Awaiting(() => Deploy(deployer))
            .Should()
            .ThrowAsync<HttpRequestException>();

        cluster
            .Calls.Should()
            .Contain(
                c =>
                    c.StartsWith("wait-job", StringComparison.Ordinal)
                    && c.Contains("-resume-", StringComparison.Ordinal),
                "the services rolled, so the hosts must not be left drained"
            );
    }

    [Test]
    public async Task A_rolled_back_deploy_leaves_the_previous_egress_image_allowed()
    {
        var (deployer, cluster) = Deployer();
        cluster.Deployments["hub"] = HeldBefore(PreviousImage);
        cluster.FailRollout = "Deployment/hub";

        await FluentActions
            .Awaiting(() => Deploy(deployer))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*rolled back*");

        EgressImages(cluster.Applied.OfType<V1ValidatingAdmissionPolicy>().Last())
            .Should()
            .Contain(PreviousImage);
    }

    [Test]
    public async Task A_deploy_on_the_same_egress_image_applies_the_policy_once()
    {
        var (deployer, cluster) = Deployer();
        cluster.Deployments["hub"] = HeldBefore(Image);

        await Deploy(deployer);

        cluster.Applied.OfType<V1ValidatingAdmissionPolicy>().Should().ContainSingle();
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
