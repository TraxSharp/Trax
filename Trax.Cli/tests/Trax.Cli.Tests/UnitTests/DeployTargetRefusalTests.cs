using AwesomeAssertions;
using Trax.Cli.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Each target refuses the fields it does not read and the values it cannot deploy, naming the problem, so a field
/// meant for another target is never ignored without a word.
/// </summary>
public class DeployTargetRefusalTests
{
    private static DeployFile File_(DeployEnvironment env)
    {
        var file = DeployKubernetesTests.File_();
        file.Settings.Clear();
        file.Environments["staging"] = env;
        return file;
    }

    private static DeployEnvironment Compose() => new() { Target = DeployEnvironment.Compose };

    private static DeployEnvironment Aws() =>
        new()
        {
            Target = DeployEnvironment.Aws,
            Profile = DeployEnvironment.Try,
            Region = "us-west-2",
        };

    private static DeployEnvironment Kubernetes() =>
        new()
        {
            Target = DeployEnvironment.Kubernetes,
            Profile = DeployEnvironment.Try,
            Database = new DeployEnvironmentDatabase { InCluster = true },
        };

    private static void Refused(DeployFile file, string message) =>
        FluentActions
            .Invoking(() => file.Environment("staging"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"Environment 'staging': {message}");

    [Test]
    public void Each_target_accepts_its_plain_environment()
    {
        foreach (var env in new[] { Compose(), Aws(), Kubernetes() })
            File_(env).Environment("staging").Should().BeSameAs(env);
    }

    [Test]
    public void A_target_trax_does_not_know_is_refused() =>
        Refused(
            File_(new DeployEnvironment { Target = "kubernets" }),
            "*'kubernets'*compose, aws or kubernetes*"
        );

    [TestCase("region", "*region is not a field a compose environment reads*")]
    [TestCase("domain", "*domain is not a field a compose environment reads*")]
    [TestCase("hostedZoneId", "*hostedZoneId is not a field a compose environment reads*")]
    [TestCase("databaseSecretArn", "*databaseSecretArn is not a field a compose*")]
    [TestCase("network", "*network is an aws field*")]
    [TestCase("auth", "*auth is an aws field*")]
    [TestCase("namespace", "*namespace is not a field a compose environment reads*")]
    [TestCase("registry", "*registry is not a field a compose environment reads*")]
    [TestCase("database", "*database is for a kubernetes environment*")]
    [TestCase("engine", "*engine 'lxc' is not podman or docker*")]
    [TestCase("settings", "*settings are for aws and kubernetes*")]
    [TestCase("counts", "*counts are for aws and kubernetes*")]
    public void A_compose_environment_refuses_what_it_does_not_read(string what, string message)
    {
        var env = Compose();
        switch (what)
        {
            case "region":
                env.Region = "us-west-2";
                break;
            case "domain":
                env.Domain = "orders.example.com";
                break;
            case "hostedZoneId":
                env.HostedZoneId = "Z123";
                break;
            case "databaseSecretArn":
                env.DatabaseSecretArn = "arn:aws:secretsmanager:us-west-2:1:secret:db";
                break;
            case "network":
                env.Network = new DeployNetwork();
                break;
            case "auth":
                env.Auth = new DeployAuth();
                break;
            case "namespace":
                env.Namespace = "orders";
                break;
            case "registry":
                env.Registry = "ghcr.io/acme";
                break;
            case "database":
                env.Database = new DeployEnvironmentDatabase { InCluster = true };
                break;
            case "engine":
                env.Engine = "lxc";
                break;
            case "settings":
                env.Settings["Auth__Authority"] = "https://issuer.example.com";
                break;
            case "counts":
                env.Counts["worker"] = 3;
                break;
        }

        Refused(File_(env), message);
    }

    [TestCase("engine", "*engine is not a field a aws environment reads*")]
    [TestCase("namespace", "*namespace is not a field a aws environment reads*")]
    [TestCase("registry", "*registry is not a field a aws environment reads*")]
    [TestCase("database", "*database is for a kubernetes environment. On aws*")]
    [TestCase("no region", "*names no region*")]
    [TestCase("production without a domain", "*production profile needs a domain*")]
    [TestCase("try with a domain", "*try profile*takes no domain*")]
    [TestCase("network without subnets", "*at least two public and two private subnets*")]
    [TestCase("auth kind", "*auth kind 'okta'*")]
    public void An_aws_environment_refuses_what_it_does_not_read(string what, string message)
    {
        var env = Aws();
        switch (what)
        {
            case "engine":
                env.Engine = DeployEnvironment.Podman;
                break;
            case "namespace":
                env.Namespace = "orders";
                break;
            case "registry":
                env.Registry = "ghcr.io/acme";
                break;
            case "database":
                env.Database = new DeployEnvironmentDatabase { InCluster = true };
                break;
            case "no region":
                env.Region = null;
                break;
            case "production without a domain":
                env.Profile = DeployEnvironment.Production;
                break;
            case "try with a domain":
                env.Domain = "orders.example.com";
                break;
            case "network without subnets":
                env.Network = new DeployNetwork { VpcId = "vpc-1", Cidr = "10.0.0.0/16" };
                break;
            case "auth kind":
                env.Auth = new DeployAuth { Kind = "okta" };
                break;
        }

        Refused(File_(env), message);
    }

    [TestCase("service name", "*service 'Hub_1' is not a Kubernetes object name*")]
    [TestCase("derived namespace", "*its namespace would be*Name one with \"namespace\"*")]
    [TestCase("secret key", "*secret 'Stripe ApiKey' cannot be a key of a Kubernetes Secret*")]
    [TestCase("database secret name", "*database needs secret and key*")]
    [TestCase("database key", "*database needs secret and key*")]
    public void A_kubernetes_environment_refuses_names_the_cluster_would(
        string what,
        string message
    )
    {
        var file = File_(Kubernetes());
        switch (what)
        {
            case "service name":
                file.Services["Hub_1"] = new DeployService { Project = "Orders/Orders.csproj" };
                break;
            case "derived namespace":
                file.App = new string('a', 60);
                break;
            case "secret key":
                file.Secrets = ["Stripe ApiKey"];
                break;
            case "database secret name":
                file.Environments["staging"].Database = new DeployEnvironmentDatabase
                {
                    Secret = "Orders_DB",
                    Key = "connection",
                };
                break;
            case "database key":
                file.Environments["staging"].Database = new DeployEnvironmentDatabase
                {
                    Secret = "orders-db",
                };
                break;
        }

        Refused(file, message);
    }
}
