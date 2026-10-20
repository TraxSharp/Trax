using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The CloudFormation template <c>trax deploy</c> generates holds to the rules a stack is reviewed against: nothing
/// reaches a task or the database but through its security group, every group replaces the default allow-all egress,
/// the database is private and encrypted, no IAM statement grants every action, and no secret value is written into
/// the template. It also refuses what the stack cannot give the hosts.
/// </summary>
public class AwsStackTemplateTests
{
    private const string PrefixList = "pl-3b927c52";

    internal static DeployFile File_(string profile = DeployEnvironment.Production)
    {
        var file = new DeployFile
        {
            App = "orders",
            Services =
            {
                ["hub"] = new DeployService { Project = "Orders/Orders.csproj" },
                ["worker"] = new DeployService
                {
                    Project = "Orders.Worker/Orders.Worker.csproj",
                    Count = 2,
                },
            },
            Settings = ["Storefront__Origin"],
            Secrets = ["Stripe__ApiKey"],
        };
        file.Environments["production"] = new DeployEnvironment
        {
            Profile = profile,
            Region = "us-west-2",
            Domain = profile == DeployEnvironment.Production ? "orders.example.com" : null,
            HostedZoneId = profile == DeployEnvironment.Production ? "Z0123456789" : null,
            Auth = new DeployAuth(),
            Settings = { ["Storefront__Origin"] = "https://shop.example.com" },
            Counts = { ["worker"] = 3 },
        };
        return file;
    }

    internal static Dictionary<string, DeployManifest> Exports() =>
        new()
        {
            ["hub"] = new DeployManifest
            {
                Roles = ["api", "scheduler"],
                Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
                Endpoints = new DeployEndpoints
                {
                    GraphQL = "/trax/graphql",
                    Health = "/trax/health",
                },
            },
            ["worker"] = new DeployManifest
            {
                Roles = ["worker"],
                Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
            },
        };

    private static JsonObject Build(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) => AwsStack.Build(file ?? File_(), "production", exports ?? Exports(), PrefixList);

    private static IEnumerable<(string Id, JsonObject Resource)> Resources(
        JsonObject template,
        string type
    ) =>
        ((JsonObject)template["Resources"]!)
            .Where(r => (string)r.Value!["Type"]! == type)
            .Select(r => (r.Key, (JsonObject)r.Value!));

    private static JsonObject Properties(JsonObject template, string id) =>
        (JsonObject)template["Resources"]![id]!["Properties"]!;

    [TestCase(DeployEnvironment.Production)]
    [TestCase(DeployEnvironment.Try)]
    public void Only_the_load_balancer_takes_traffic_from_outside(string profile)
    {
        var template = Build(File_(profile));

        foreach (var (id, group) in Resources(template, "AWS::EC2::SecurityGroup"))
        foreach (var rule in group["Properties"]!["SecurityGroupIngress"]?.AsArray() ?? [])
        {
            var open =
                (string?)rule!["CidrIp"] == "0.0.0.0/0" || (string?)rule["CidrIpv6"] == "::/0";
            if (!open)
                continue;
            id.Should()
                .Be("LoadBalancerSecurityGroup", "only the load balancer is open to the internet");
            profile.Should().Be(DeployEnvironment.Production, "try admits CloudFront alone");
            ((int)rule["FromPort"]!).Should().BeOneOf(80, 443);
        }
        foreach (var (_, rule) in Resources(template, "AWS::EC2::SecurityGroupIngress"))
            rule["Properties"]!
                ["SourceSecurityGroupId"]
                .Should()
                .NotBeNull("every other way in names a group");
    }

    [Test]
    public void The_try_load_balancer_admits_CloudFront_alone()
    {
        var ingress = Properties(Build(File_(DeployEnvironment.Try)), "LoadBalancerSecurityGroup")[
            "SecurityGroupIngress"
        ]!.AsArray();

        ingress.Select(r => (string?)r!["SourcePrefixListId"]).Should().Equal(PrefixList);
    }

    [TestCase(DeployEnvironment.Production)]
    [TestCase(DeployEnvironment.Try)]
    public void Every_security_group_replaces_the_default_egress(string profile)
    {
        foreach (var (id, group) in Resources(Build(File_(profile)), "AWS::EC2::SecurityGroup"))
            group["Properties"]!
                ["SecurityGroupEgress"]
                .Should()
                .NotBeNull($"{id} would otherwise allow all egress");
    }

    [TestCase(DeployEnvironment.Production, true)]
    [TestCase(DeployEnvironment.Try, false)]
    public void The_database_is_private_and_encrypted(string profile, bool multiAz)
    {
        var template = Build(File_(profile));
        var database = Properties(template, "Database");

        ((bool)database["PubliclyAccessible"]!).Should().BeFalse();
        ((bool)database["StorageEncrypted"]!).Should().BeTrue();
        ((bool)database["MultiAZ"]!).Should().Be(multiAz);
        ((string)template["Resources"]!["Database"]!["DeletionPolicy"]!)
            .Should()
            .Be(multiAz ? "Snapshot" : "Delete");
    }

    [Test]
    public void No_IAM_statement_grants_every_action()
    {
        var text = AwsStack.Compact(Build());

        foreach (var (_, role) in Resources(Build(), "AWS::IAM::Role"))
        foreach (var policy in role["Properties"]!["Policies"]?.AsArray() ?? [])
        foreach (var statement in policy!["PolicyDocument"]!["Statement"]!.AsArray())
            statement!["Action"]!
                .AsArray()
                .Select(a => (string?)a)
                .Should()
                .NotContain(a => a!.Contains('*'));
        text.Should().NotContain("\"Action\":\"*\"");
    }

    [Test]
    public void No_secret_value_is_in_the_template_and_secrets_reach_tasks_by_reference()
    {
        var template = Build();

        foreach (var (id, task) in Resources(template, "AWS::ECS::TaskDefinition"))
        {
            var container = task["Properties"]!["ContainerDefinitions"]![0]!;
            var names = container["Environment"]!
                .AsArray()
                .Select(e => (string)e!["Name"]!)
                .ToList();
            names
                .Should()
                .NotContain(
                    ["Stripe__ApiKey", "PGPASSWORD"],
                    $"{id} must take secrets by reference"
                );
            container["Secrets"]!
                .AsArray()
                .Select(s => (string)s!["Name"]!)
                .Should()
                .Contain(["Stripe__ApiKey", "PGPASSWORD"]);
        }
        var database = Properties(template, "Database");
        database["MasterUserPassword"]!
            .ToJsonString()
            .Should()
            .Contain("{{resolve:secretsmanager:");
    }

    [Test]
    public void Production_serves_HTTPS_on_its_domain_and_redirects_HTTP()
    {
        var template = Build();

        Properties(template, "HttpsListener")["Protocol"]!.GetValue<string>().Should().Be("HTTPS");
        Properties(template, "HttpListener")["DefaultActions"]![0]!["Type"]!
            .GetValue<string>()
            .Should()
            .Be("redirect");
        Properties(template, "Certificate")["DomainName"]!
            .GetValue<string>()
            .Should()
            .Be("orders.example.com");
        Properties(template, "DomainRecord")["Name"]!
            .GetValue<string>()
            .Should()
            .Be("orders.example.com");
        template["Outputs"]!["Url"]!["Value"]!
            .GetValue<string>()
            .Should()
            .Be("https://orders.example.com");
    }

    [Test]
    public void Try_serves_on_a_CloudFront_address_that_redirects_to_HTTPS()
    {
        var template = Build(File_(DeployEnvironment.Try));

        Properties(template, "Distribution")["DistributionConfig"]!["DefaultCacheBehavior"]![
            "ViewerProtocolPolicy"
        ]!
            .GetValue<string>()
            .Should()
            .Be("redirect-to-https");
        ((JsonObject)template["Resources"]!).ContainsKey("NatGateway").Should().BeFalse();
        ((JsonObject)template["Resources"]!).ContainsKey("Certificate").Should().BeFalse();
    }

    [Test]
    public void Services_wait_at_zero_until_the_stack_is_running_and_run_their_counts_after()
    {
        var template = Build();

        Properties(template, "HubService")["DesiredCount"]!
            .ToJsonString()
            .Should()
            .Be("""{"Fn::If":["Running",1,0]}""");
        Properties(template, "WorkerService")["DesiredCount"]!
            .ToJsonString()
            .Should()
            .Be("""{"Fn::If":["Running",3,0]}""", "the environment's count overrides the file's");
    }

    [Test]
    public void The_serving_tasks_skip_migrations_and_the_migrate_task_runs_them()
    {
        var template = Build();
        string[] Env(string id) =>
            [
                .. Properties(template, id)["ContainerDefinitions"]![0]!["Environment"]!
                    .AsArray()
                    .Select(e => $"{e!["Name"]}={e["Value"]!.ToJsonString().Trim('"')}"),
            ];

        Env("HubTaskDefinition").Should().Contain("TRAX_MIGRATIONS=skip");
        Env("MigrateTaskDefinition").Should().NotContain("TRAX_MIGRATIONS=skip");
        Properties(template, "MigrateTaskDefinition")["ContainerDefinitions"]![0]!["Command"]![0]!
            .GetValue<string>()
            .Should()
            .Be("--trax-migrate");
        Env("HubTaskDefinition").Should().Contain("Trax__Proxy__KnownNetworks=10.0.0.0/16");
    }

    [Test]
    public void Cognito_gives_the_hosts_its_issuer_and_client()
    {
        var template = Build();
        var env = Properties(template, "HubTaskDefinition")["ContainerDefinitions"]![0]![
            "Environment"
        ]!.AsArray();

        Resources(template, "AWS::Cognito::UserPool").Should().ContainSingle();
        env.Single(e => (string)e!["Name"]! == "Auth__Authority")!["Value"]!
            .ToJsonString()
            .Should()
            .Contain("cognito-idp.${AWS::Region}.amazonaws.com/${UserPool}");
        env.Single(e => (string)e!["Name"]! == "Auth__Audience")!["Value"]!
            .ToJsonString()
            .Should()
            .Be("""{"Ref":"UserPoolClient"}""");
    }

    [Test]
    public void An_existing_network_creates_no_VPC()
    {
        var file = File_();
        file.Environments["production"].Network = new DeployNetwork
        {
            VpcId = "vpc-0abc",
            Cidr = "172.16.0.0/16",
            PublicSubnets = ["subnet-1", "subnet-2"],
            PrivateSubnets = ["subnet-3", "subnet-4"],
        };

        var template = Build(file);

        Resources(template, "AWS::EC2::VPC").Should().BeEmpty();
        Resources(template, "AWS::EC2::NatGateway").Should().BeEmpty();
    }

    [Test]
    public void It_fits_CloudFormations_inline_limit() =>
        AwsStack.Compact(Build()).Length.Should().BeLessThan(AwsDeployer.InlineTemplateLimit);

    [Test]
    public void Production_without_a_domain_is_refused()
    {
        var file = File_();
        file.Environments["production"].Domain = null;

        var act = () => Build(file);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*production profile needs a domain*");
    }

    [Test]
    public void A_host_that_connects_to_RabbitMQ_is_refused()
    {
        var exports = Exports();
        exports["worker"].Transports = ["rabbitmq"];

        var act = () => Build(exports: exports);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'worker'*RabbitMQ*");
    }

    [Test]
    public void An_API_service_with_no_health_check_is_refused()
    {
        var exports = Exports();
        exports["hub"].Endpoints.Health = null;

        var act = () => Build(exports: exports);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'hub' maps no health check*");
    }

    [Test]
    public void A_setting_with_no_value_in_the_environment_is_refused()
    {
        var file = File_();
        file.Environments["production"].Settings.Clear();

        var act = () => Build(file);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*no value for Storefront__Origin*");
    }

    [Test]
    public void The_plan_summary_counts_resources_and_estimates_the_idle_cost()
    {
        var lines = AwsPlanSummary.Lines(File_(), "production", Build()).ToList();

        lines[0].Should().StartWith("orders-production (production, us-west-2) has ");
        lines.Should().Contain(l => l.Contains("NAT gateway"));
        lines.Last().Should().EndWith("a month");
    }
}
