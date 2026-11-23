using System.Text.Json.Nodes;
using AwesomeAssertions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A kubernetes environment with an <c>aws</c> block goes onto an EKS cluster trax does not create: the AWS foundation
/// gives it its database and secrets, the pods reach them through a Pod Identity role and, unless
/// <c>"databaseAccess": "nodes"</c> says otherwise, Security Groups for Pods, and no secret value becomes a Kubernetes
/// Secret. Nothing is pushed or deployed until the hosts read their secrets from AWS, the kubeconfig context is the
/// named cluster, and every app secret has a value.
/// </summary>
public class EksDeployTests
{
    private const string Tag = "20261123-120000-abc";
    private const string Endpoint = "https://ABC123.gr7.us-east-1.eks.amazonaws.com";

    internal static DeployFile File_(string access = DeployEnvironmentAws.Pods)
    {
        var file = DeployKubernetesTests.File_();
        file.Environments.Clear();
        file.Environments["staging"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Kubernetes,
            Profile = DeployEnvironment.Try,
            Settings = { ["Auth__Authority"] = "https://issuer.example.com" },
            Eks = new DeployEnvironmentAws
            {
                Region = "us-east-1",
                Cluster = "apps",
                DatabaseAccess = access,
                Network = new DeployNetwork
                {
                    VpcId = "vpc-1",
                    Cidr = "10.0.0.0/16",
                    PrivateSubnets = ["subnet-1", "subnet-2"],
                },
            },
        };
        return file;
    }

    internal static Dictionary<string, DeployManifest> Exports(bool readsAwsSecrets = true)
    {
        var exports = DeployKubernetesTests.Exports();
        foreach (var export in exports.Values)
            export.SecretSources = readsAwsSecrets ? ["aws-secrets"] : [];
        return exports;
    }

    private static EksTarget Target(bool pods = true) =>
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["hub"] = $"123.dkr.ecr.us-east-1.amazonaws.com/trax/orders-staging/hub:{Tag}",
                ["worker"] =
                    $"123.dkr.ecr.us-east-1.amazonaws.com/trax/orders-staging/worker:{Tag}",
                ["reports"] =
                    $"123.dkr.ecr.us-east-1.amazonaws.com/trax/orders-staging/reports:{Tag}",
            },
            "us-east-1",
            "db.example",
            "5432",
            "arn:db",
            "sg-client",
            "sg-cluster",
            pods
        );

    private static KubernetesManifests Render(bool pods = true) =>
        KubernetesTarget.Render(File_(), "staging", Exports(), Tag, Target(pods));

    private static string Yaml(KubernetesManifests m) =>
        string.Join("\n---\n", m.All.Select(KubernetesYaml.Serialize));

    // ---- The file -------------------------------------------------------------------------------------

    [TestCase("cluster", "*aws needs region and cluster*")]
    [TestCase("database", "*database is the foundation's on EKS*")]
    [TestCase("registry", "*registry is not read on EKS*")]
    [TestCase("access", "*aws.databaseAccess 'everyone' is not pods or nodes*")]
    [TestCase("subnets", "*at least two private subnets*")]
    [TestCase("podman", "*aws is for a cluster, not a podman environment*")]
    public void An_eks_environment_that_cannot_deploy_is_refused(string what, string message)
    {
        var file = File_();
        var env = file.Environments["staging"];
        var aws = env.Eks.NotNull();
        switch (what)
        {
            case "cluster":
                aws.Cluster = null;
                break;
            case "database":
                env.Database = new DeployEnvironmentDatabase { InCluster = true };
                break;
            case "registry":
                env.Registry = "ghcr.io/acme";
                break;
            case "access":
                aws.DatabaseAccess = "everyone";
                break;
            case "subnets":
                aws.Network.NotNull().PrivateSubnets = ["subnet-1"];
                break;
            case "podman":
                env.Runtime = DeployEnvironment.Podman;
                break;
        }

        FluentActions
            .Invoking(() => file.Environment("staging"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(message);
    }

    [Test]
    public void A_network_needs_no_public_subnets_on_eks() =>
        File_()
            .Environment("staging")
            .Eks.NotNull()
            .Network.NotNull()
            .PublicSubnets.Should()
            .BeEmpty();

    // ---- The foundation -------------------------------------------------------------------------------

    private static JsonObject Resources(string? nodeGroup = null) =>
        AwsStack
            .EksFoundation(File_(), "staging", "orders-staging", nodeGroup)["Resources"]
            .NotNull()
            .AsObject();

    [Test]
    public void The_foundation_goes_into_the_clusters_vpc_and_admits_only_the_client_group()
    {
        var resources = Resources();

        resources
            .Should()
            .NotContain(r =>
                string.Equals(Type(r.Value), "AWS::EC2::VPC", StringComparison.Ordinal)
            );
        resources["DatabaseFromClients"]
            .NotNull()["Properties"]
            .NotNull()["SourceSecurityGroupId"]
            .NotNull()["Ref"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("DatabaseClientSecurityGroup");
        resources.ContainsKey("DatabaseFromNodes").Should().BeFalse();
    }

    [Test]
    public void Nodes_are_admitted_only_when_the_environment_says_so()
    {
        var ingress = Resources("sg-cluster")["DatabaseFromNodes"]
            .NotNull()["Properties"]
            .NotNull();

        ingress["SourceSecurityGroupId"].NotNull().GetValue<string>().Should().Be("sg-cluster");
        ingress["FromPort"].NotNull().GetValue<int>().Should().Be(5432);
    }

    [Test]
    public void The_pods_role_is_trusted_only_by_pod_identity_and_reads_only_this_environments_secrets()
    {
        var role = Resources()["PodRole"].NotNull()["Properties"].NotNull();

        var trust = role["AssumeRolePolicyDocument"].NotNull()["Statement"].NotNull()[0].NotNull();
        trust["Principal"]
            .NotNull()["Service"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("pods.eks.amazonaws.com");
        var statement = role["Policies"]
            .NotNull()[0]
            .NotNull()["PolicyDocument"]
            .NotNull()["Statement"]
            .NotNull()[0]
            .NotNull();
        statement["Action"]
            .NotNull()
            .AsArray()
            .Select(a => a.NotNull().GetValue<string>())
            .Should()
            .Equal("secretsmanager:GetSecretValue");
        statement["Resource"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Contain("\"Ref\":\"DatabaseSecret\"")
            .And.Contain("secret:orders-staging/app/*");
    }

    [Test]
    public void Each_service_account_is_bound_to_the_pods_role()
    {
        var associations = Resources()
            .Where(r =>
                string.Equals(
                    Type(r.Value),
                    "AWS::EKS::PodIdentityAssociation",
                    StringComparison.Ordinal
                )
            )
            .Select(r => r.Value.NotNull()["Properties"].NotNull())
            .ToList();

        associations
            .Select(a => a["ServiceAccount"].NotNull().GetValue<string>())
            .Should()
            .BeEquivalentTo(["hub", "reports", "worker"]);
        associations
            .Should()
            .OnlyContain(a =>
                string.Equals(
                    a["ClusterName"].NotNull().GetValue<string>(),
                    "apps",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    a["Namespace"].NotNull().GetValue<string>(),
                    "orders-staging",
                    StringComparison.Ordinal
                )
            );
    }

    private static string? Type(JsonNode? resource) => resource?["Type"]?.GetValue<string>();

    // ---- The manifests --------------------------------------------------------------------------------

    [Test]
    public void No_secret_value_or_reference_is_in_the_manifests()
    {
        var yaml = Yaml(Render());

        yaml.Should()
            .NotContain("secretKeyRef")
            .And.NotContain("kind: Secret")
            .And.NotContain("PGPASSWORD");
    }

    [Test]
    public void Each_service_runs_as_its_own_account_with_its_image_from_ecr()
    {
        var m = Render();

        m.Backing.OfType<V1ServiceAccount>()
            .Select(a => a.Metadata.Name)
            .Should()
            .BeEquivalentTo(["hub", "reports", "worker"]);
        m.Backing.OfType<V1ServiceAccount>()
            .Should()
            .OnlyContain(a => a.AutomountServiceAccountToken == false);
        foreach (var d in m.Services.OfType<V1Deployment>())
        {
            d.Spec.Template.Spec.ServiceAccountName.Should().Be(d.Metadata.Name);
            d.Spec.Template.Spec.Containers[0]
                .Image.Should()
                .StartWith("123.dkr.ecr.us-east-1.amazonaws.com/trax/orders-staging/");
        }
        m.Migrate.Spec.Template.Spec.ServiceAccountName.Should().Be("hub");
    }

    [Test]
    public void The_host_is_told_where_its_password_and_secrets_are_and_not_what_they_are()
    {
        var env = Render()
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, "hub", StringComparison.Ordinal))
            .Spec.Template.Spec.Containers[0]
            .Env.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);

        env["ConnectionStrings__Trax"]
            .Should()
            .Be(
                "Host=db.example;Port=5432;Database=trax;Username=trax;SSL Mode=VerifyFull;"
                    + "Root Certificate=/app/trax/rds-global-bundle.pem;GSS Encryption Mode=Disable"
            );
        env["Trax__AwsSecrets__0__SecretId"].Should().Be("arn:db");
        env["Trax__AwsSecrets__0__JsonKey"].Should().Be("password");
        env["Trax__AwsSecrets__0__PasswordOf"].Should().Be("Trax");
        env["Trax__AwsSecrets__1__SecretId"].Should().Be("orders-staging/app/Stripe__ApiKey");
        env["Trax__AwsSecrets__1__Key"].Should().Be("Stripe__ApiKey");
        env["AWS_REGION"].Should().Be("us-east-1");
        env["Auth__Authority"].Should().Be("https://issuer.example.com");
    }

    [Test]
    public void The_apps_pods_wear_the_client_group_beside_the_clusters_own()
    {
        var policy = Render().Backing.OfType<EksSecurityGroupPolicy>().Single();

        policy.Spec.PodSelector.MatchLabels.Should().Contain(KubernetesTarget.AppLabel, "orders");
        policy.Spec.SecurityGroups.GroupIds.Should().Equal("sg-client", "sg-cluster");
        KubernetesYaml
            .Serialize(policy)
            .Should()
            .Contain("apiVersion: vpcresources.k8s.aws/v1beta1")
            .And.Contain("groupIds:");
    }

    [Test]
    public void With_node_access_no_security_group_policy_is_rendered() =>
        Render(pods: false).Backing.OfType<EksSecurityGroupPolicy>().Should().BeEmpty();

    [Test]
    public void RabbitMQ_is_refused_on_eks()
    {
        var act = () =>
            KubernetesTarget.Render(
                File_(),
                "staging",
                DeployKubernetesTests.Exports(rabbit: true),
                Tag,
                Target()
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMQ*on EKS*");
    }

    [Test]
    public async Task Secret_set_stores_an_eks_environments_secret_where_its_pods_read_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-eks-{Guid.NewGuid():N}");
        var file = File_();
        foreach (var service in file.Services.Values)
        {
            var project = Path.Combine(dir, service.Project);
            Directory.CreateDirectory(Path.GetDirectoryName(project) ?? dir);
            await File.WriteAllTextAsync(project, "<Project />");
        }
        var path = Path.Combine(dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write());
        var aws = new AwsDeployerTests.FakeAws();
        var regions = new List<string>();
        var original = Trax.Cli.Commands.DeployAwsCommands.Client;
        Trax.Cli.Commands.DeployAwsCommands.Client = region =>
        {
            regions.Add(region);
            return aws;
        };
        try
        {
            (
                await Trax.Cli.Commands.DeployAwsCommands.SecretSetAsync(
                    path,
                    "staging",
                    "Stripe__ApiKey",
                    () => Task.FromResult("sk_test"),
                    TextWriter.Null,
                    TextWriter.Null,
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
        }
        finally
        {
            Trax.Cli.Commands.DeployAwsCommands.Client = original;
            Directory.Delete(dir, recursive: true);
        }

        aws.Secrets.Should().Contain("orders-staging/app/Stripe__ApiKey");
        regions.Should().OnlyContain(r => r == "us-east-1");
    }

    // ---- The deploy -----------------------------------------------------------------------------------

    private sealed class FakeEks(EksCluster? cluster) : IEksClient
    {
        public List<string> Asked { get; } = [];

        public Task<EksCluster?> DescribeClusterAsync(string name, CancellationToken ct)
        {
            Asked.Add(name);
            return Task.FromResult(cluster);
        }
    }

    private static EksCluster Cluster(string vpc = "vpc-1", string status = "ACTIVE") =>
        new("apps", status, Endpoint, vpc, "sg-cluster");

    private sealed record Run(
        EksDeployer Deployer,
        AwsDeployerTests.FakeAws Aws,
        AwsDeployerTests.FakeImages Images,
        KubernetesDeployerTests.FakeCluster Kube,
        List<string> Said
    );

    private static Run Deployer(EksCluster? cluster = null, string server = Endpoint)
    {
        var aws = new AwsDeployerTests.FakeAws();
        aws.Secrets.Add("orders-staging/app/Stripe__ApiKey");
        var images = new AwsDeployerTests.FakeImages();
        var kube = new KubernetesDeployerTests.FakeCluster { Server = server };
        var said = new List<string>();
        return new Run(
            new EksDeployer(aws, new FakeEks(cluster), kube, images, said.Add),
            aws,
            images,
            kube,
            said
        );
    }

    private static Task Deploy(
        Run run,
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) =>
        run.Deployer.DeployAsync(
            file ?? File_(),
            "/src/trax.deploy.json",
            "staging",
            exports ?? Exports(),
            Tag,
            CancellationToken.None
        );

    [Test]
    public async Task A_deploy_pushes_to_ecr_deploys_the_foundation_then_applies_the_manifests()
    {
        var run = Deployer(Cluster());

        await Deploy(run);

        run.Images.Published.Should().BeEquivalentTo(["hub", "reports", "worker"]);
        run.Aws.Calls.Should().Equal("deploy-base");
        run.Aws.Stacks.Should().Equal("orders-staging-base");
        run.Kube.Calls.Should()
            .Contain("apply ServiceAccount/hub")
            .And.Contain("apply SecurityGroupPolicy/orders-database-clients");
        run.Kube.Calls.Should().NotContain(c => c.StartsWith("secret", StringComparison.Ordinal));
        run.Kube.Calls.IndexOf("apply SecurityGroupPolicy/orders-database-clients")
            .Should()
            .BeLessThan(
                run.Kube.Calls.FindIndex(c => c.StartsWith("create-job", StringComparison.Ordinal))
            );
        var template = JsonNode.Parse(run.Aws.Templates.Single().NotNull()).NotNull();
        template["Resources"].NotNull().AsObject().Should().ContainKey("HubPodIdentity");
    }

    [Test]
    public async Task A_host_that_does_not_read_its_secrets_from_aws_is_refused_before_anything_changes()
    {
        var run = Deployer(Cluster());

        var act = () => Deploy(run, exports: Exports(readsAwsSecrets: false));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*on EKS*hub, reports, worker does not*AddTraxAwsSecretsAsync*Nothing was deployed*"
        );
        run.Aws.Calls.Should().BeEmpty();
        run.Images.Published.Should().BeEmpty();
        run.Kube.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task A_context_that_is_not_the_named_cluster_is_refused_before_anything_changes()
    {
        var run = Deployer(Cluster(), server: "https://kind-test:6443");

        var act = () => Deploy(run);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*points at https://kind-test:6443, not cluster apps*aws eks update-kubeconfig --region us-east-1 --name apps*"
        );
        run.Aws.Calls.Should().BeEmpty();
        run.Images.Published.Should().BeEmpty();
        run.Kube.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task A_cluster_outside_the_named_vpc_is_refused()
    {
        var run = Deployer(Cluster(vpc: "vpc-other"));

        var act = () => Deploy(run);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*names vpc-1, and cluster apps is in vpc-other*"
        );
        run.Aws.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task A_cluster_that_is_not_active_is_refused()
    {
        var run = Deployer(Cluster(status: "CREATING"));

        var act = () => Deploy(run);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*apps is CREATING*"
        );
    }

    [Test]
    public async Task A_secret_without_a_value_is_refused_before_anything_is_pushed()
    {
        var run = Deployer(Cluster());
        run.Aws.Secrets.Clear();

        var act = () => Deploy(run);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*no value for Stripe__ApiKey in Secrets Manager*trax deploy secret set*"
        );
        run.Images.Published.Should().BeEmpty();
        run.Aws.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task Without_a_cluster_or_a_network_the_foundation_is_deployed_and_the_cluster_is_asked_for()
    {
        var run = Deployer(cluster: null);
        var file = File_();
        file.Environments["staging"].Eks.NotNull().Network = null;

        await Deploy(run, file);

        run.Aws.Calls.Should().Equal("deploy-base");
        run.Kube.Calls.Should().BeEmpty();
        run.Images.Published.Should().BeEmpty();
        run.Said[^1]
            .Should()
            .Contain("no EKS cluster apps")
            .And.Contain("create the cluster in vpc-1");
    }

    [Test]
    public async Task Without_a_cluster_in_a_named_network_nothing_is_deployed()
    {
        var run = Deployer(cluster: null);

        var act = () => Deploy(run);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*no EKS cluster apps in us-east-1*never creates one*"
        );
        run.Aws.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task Node_access_admits_the_clusters_group_in_the_foundation_and_renders_no_policy()
    {
        var run = Deployer(Cluster());

        await Deploy(run, File_(DeployEnvironmentAws.Nodes));

        var template = JsonNode.Parse(run.Aws.Templates.Single().NotNull()).NotNull();
        template["Resources"]
            .NotNull()["DatabaseFromNodes"]
            .NotNull()["Properties"]
            .NotNull()["SourceSecurityGroupId"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("sg-cluster");
        run.Kube.Calls.Should()
            .NotContain(c => c.Contains("SecurityGroupPolicy", StringComparison.Ordinal));
    }
}
