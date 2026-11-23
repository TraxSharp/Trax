using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;
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
            Domain = string.Equals(profile, DeployEnvironment.Production, StringComparison.Ordinal)
                ? "orders.example.com"
                : null,
            HostedZoneId = string.Equals(
                profile,
                DeployEnvironment.Production,
                StringComparison.Ordinal
            )
                ? "Z0123456789"
                : null,
            Auth = new DeployAuth(),
            Settings = { ["Storefront__Origin"] = "https://shop.example.com" },
            Counts = { ["worker"] = 3 },
        };
        return file;
    }

    internal static Dictionary<string, DeployManifest> Exports() =>
        new(StringComparer.Ordinal)
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

    internal static AwsTemplates Templates(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) => AwsStack.Build(file ?? File_(), "production", exports ?? Exports(), PrefixList);

    /// <summary>Both stacks' resources and outputs in one object, for the rules that hold across the two.</summary>
    private static JsonObject Build(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    )
    {
        var templates = Templates(file, exports);
        var merged = new JsonObject
        {
            ["Parameters"] = new JsonObject(),
            ["Resources"] = new JsonObject(),
            ["Outputs"] = new JsonObject(),
        };
        foreach (var template in new[] { templates.Foundation, templates.Runtime })
        foreach (var section in new[] { "Parameters", "Resources", "Outputs" })
        foreach (var (key, value) in (JsonObject?)template[section] ?? new JsonObject())
            merged[section].NotNull()[key] = value.NotNull().DeepClone();
        return merged;
    }

    private static IEnumerable<(string Id, JsonObject Resource)> Resources(
        JsonObject template,
        string type
    ) =>
        ((JsonObject)template["Resources"].NotNull())
            .Where(r =>
                string.Equals(
                    r.Value.NotNull()["Type"].NotNull().GetValue<string>(),
                    type,
                    StringComparison.Ordinal
                )
            )
            .Select(r => (r.Key, (JsonObject)r.Value.NotNull()));

    private static JsonObject Properties(JsonObject template, string id) =>
        (JsonObject)template["Resources"].NotNull()[id].NotNull()["Properties"].NotNull();

    [TestCase(DeployEnvironment.Production)]
    [TestCase(DeployEnvironment.Try)]
    public void Only_the_load_balancer_takes_traffic_from_outside(string profile)
    {
        var template = Build(File_(profile));

        foreach (var (id, group) in Resources(template, "AWS::EC2::SecurityGroup"))
        foreach (var rule in group["Properties"].NotNull()["SecurityGroupIngress"]?.AsArray() ?? [])
        {
            var open =
                string.Equals(
                    (string?)rule.NotNull()["CidrIp"],
                    "0.0.0.0/0",
                    StringComparison.Ordinal
                ) || string.Equals((string?)rule["CidrIpv6"], "::/0", StringComparison.Ordinal);
            if (!open)
                continue;
            id.Should()
                .Be("LoadBalancerSecurityGroup", "only the load balancer is open to the internet");
            profile.Should().Be(DeployEnvironment.Production, "try admits CloudFront alone");
            ((int)rule["FromPort"].NotNull()).Should().BeOneOf(80, 443);
        }
        foreach (var (_, rule) in Resources(template, "AWS::EC2::SecurityGroupIngress"))
            rule["Properties"]
                .NotNull()["SourceSecurityGroupId"]
                .Should()
                .NotBeNull("every other way in names a group");
    }

    [Test]
    public void The_try_load_balancer_admits_CloudFront_alone()
    {
        var ingress = Properties(Build(File_(DeployEnvironment.Try)), "LoadBalancerSecurityGroup")[
            "SecurityGroupIngress"
        ]
            .NotNull()
            .AsArray();

        ingress.Select(r => (string?)r.NotNull()["SourcePrefixListId"]).Should().Equal(PrefixList);
    }

    [TestCase(DeployEnvironment.Production)]
    [TestCase(DeployEnvironment.Try)]
    public void Every_security_group_replaces_the_default_egress(string profile)
    {
        foreach (var (id, group) in Resources(Build(File_(profile)), "AWS::EC2::SecurityGroup"))
            group["Properties"]
                .NotNull()["SecurityGroupEgress"]
                .Should()
                .NotBeNull($"{id} would otherwise allow all egress");
    }

    [TestCase(DeployEnvironment.Production, true)]
    [TestCase(DeployEnvironment.Try, false)]
    public void The_database_is_private_and_encrypted(string profile, bool multiAz)
    {
        var template = Build(File_(profile));
        var database = Properties(template, "Database");

        ((bool)database["PubliclyAccessible"].NotNull()).Should().BeFalse();
        ((bool)database["StorageEncrypted"].NotNull()).Should().BeTrue();
        ((bool)database["MultiAZ"].NotNull()).Should().Be(multiAz);
        template["Resources"]
            .NotNull()["Database"]
            .NotNull()["DeletionPolicy"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be(multiAz ? "Snapshot" : "Delete");
    }

    [Test]
    public void The_connection_string_verifies_the_server()
    {
        var template = Build();
        var env = Properties(template, "HubTaskDefinition")["ContainerDefinitions"]
            .NotNull()[0]
            .NotNull()["Environment"]
            .NotNull()
            .AsArray();

        var connection = env.Single(e =>
                string.Equals(
                    e.NotNull()["Name"].NotNull().GetValue<string>(),
                    "ConnectionStrings__Trax",
                    StringComparison.Ordinal
                )
            )
            .NotNull()["Value"]
            .NotNull()["Fn::Sub"]
            .NotNull()
            .GetValue<string>();

        connection.Should().Contain("SSL Mode=VerifyFull;");
        connection.Should().Contain($"Root Certificate={AwsStack.RdsCaBundle};");
        AwsStack.RdsCaBundle.Should().Be("/app/trax/rds-global-bundle.pem");
    }

    [Test]
    public void No_IAM_statement_grants_every_action()
    {
        var text = AwsStack.Compact(Build());

        foreach (var (_, role) in Resources(Build(), "AWS::IAM::Role"))
        foreach (var policy in role["Properties"].NotNull()["Policies"]?.AsArray() ?? [])
        foreach (
            var statement in policy
                .NotNull()["PolicyDocument"]
                .NotNull()["Statement"]
                .NotNull()
                .AsArray()
        )
            statement
                .NotNull()["Action"]
                .NotNull()
                .AsArray()
                .Select(a => (string?)a)
                .Should()
                .NotContain(a => a.NotNull().Contains('*'));
        text.Should().NotContain("\"Action\":\"*\"");
    }

    [Test]
    public void No_secret_value_is_in_the_template_and_secrets_reach_tasks_by_reference()
    {
        var template = Build();

        foreach (var (id, task) in Resources(template, "AWS::ECS::TaskDefinition"))
        {
            var container = task["Properties"]
                .NotNull()["ContainerDefinitions"]
                .NotNull()[0]
                .NotNull();
            var names = container["Environment"]
                .NotNull()
                .AsArray()
                .Select(e => e.NotNull()["Name"].NotNull().GetValue<string>())
                .ToList();
            names
                .Should()
                .NotContain(
                    ["Stripe__ApiKey", "PGPASSWORD"],
                    $"{id} must take secrets by reference"
                );
            container["Secrets"]
                .NotNull()
                .AsArray()
                .Select(s => s.NotNull()["Name"].NotNull().GetValue<string>())
                .Should()
                .Contain(["Stripe__ApiKey", "PGPASSWORD"]);
        }
        var database = Properties(template, "Database");
        database["MasterUserPassword"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Contain("{{resolve:secretsmanager:");
    }

    [Test]
    public void Production_serves_HTTPS_on_its_domain_and_redirects_HTTP()
    {
        var template = Build();

        Properties(template, "HttpsListener")["Protocol"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("HTTPS");
        Properties(template, "HttpListener")["DefaultActions"]
            .NotNull()[0]
            .NotNull()["Type"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("redirect");
        Properties(template, "Certificate")["DomainName"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("orders.example.com");
        Properties(template, "DomainRecord")["Name"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("orders.example.com");
        template["Outputs"]
            .NotNull()["Url"]
            .NotNull()["Value"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("https://orders.example.com");
    }

    [Test]
    public void Try_serves_on_a_CloudFront_address_that_redirects_to_HTTPS()
    {
        var template = Build(File_(DeployEnvironment.Try));

        Properties(template, "Distribution")["DistributionConfig"]
            .NotNull()["DefaultCacheBehavior"]
            .NotNull()["ViewerProtocolPolicy"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("redirect-to-https");
        ((JsonObject)template["Resources"].NotNull()).ContainsKey("NatGateway").Should().BeFalse();
        ((JsonObject)template["Resources"].NotNull()).ContainsKey("Certificate").Should().BeFalse();
    }

    [Test]
    public void Each_service_runs_the_count_its_own_parameter_gives()
    {
        var template = Build();
        var parameters = (JsonObject)template["Parameters"].NotNull();

        Properties(template, "HubService")["DesiredCount"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("""{"Ref":"HubCount"}""");
        Properties(template, "WorkerService")["DesiredCount"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("""{"Ref":"WorkerCount"}""");
        parameters["HubCount"].NotNull()["Type"].NotNull().GetValue<string>().Should().Be("Number");
        parameters["HubCount"].NotNull()["Default"].NotNull().GetValue<string>().Should().Be("1");
        parameters["WorkerCount"]
            .NotNull()["Default"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("3", "the environment's count overrides the file's");
        parameters.Should().NotContainKey("Scale");
        ((JsonObject?)template["Conditions"])?.Should().NotContainKey("Running");
    }

    [Test]
    public void The_serving_tasks_skip_migrations_and_the_migrate_task_runs_them()
    {
        var template = Build();
        string[] Env(string id) =>
            [
                .. Properties(template, id)["ContainerDefinitions"]
                    .NotNull()[0]
                    .NotNull()["Environment"]
                    .NotNull()
                    .AsArray()
                    .Select(e =>
                        $"{e.NotNull()["Name"]}={e["Value"].NotNull().ToJsonString().Trim('"')}"
                    ),
            ];

        Env("HubTaskDefinition").Should().Contain("TRAX_MIGRATIONS=skip");
        Env("MigrateTaskDefinition").Should().NotContain("TRAX_MIGRATIONS=skip");
        Properties(template, "MigrateTaskDefinition")["ContainerDefinitions"]
            .NotNull()[0]
            .NotNull()["Command"]
            .NotNull()[0]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("--trax-migrate");
        Env("HubTaskDefinition")
            .Should()
            .Contain("""Trax__Proxy__KnownNetworks={"Ref":"VpcCidr"}""");
        template["Outputs"]
            .NotNull()["VpcCidr"]
            .NotNull()["Value"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("10.0.0.0/16");
    }

    [Test]
    public void Cognito_gives_the_hosts_its_issuer_and_client()
    {
        var template = Build();
        var env = Properties(template, "HubTaskDefinition")["ContainerDefinitions"]
            .NotNull()[0]
            .NotNull()["Environment"]
            .NotNull()
            .AsArray();

        Resources(template, "AWS::Cognito::UserPool").Should().ContainSingle();
        env.Single(e =>
                string.Equals(
                    e.NotNull()["Name"].NotNull().GetValue<string>(),
                    "Auth__Authority",
                    StringComparison.Ordinal
                )
            )
            .NotNull()["Value"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Contain("cognito-idp.${AWS::Region}.amazonaws.com/${UserPoolId}");
        env.Single(e =>
                string.Equals(
                    e.NotNull()["Name"].NotNull().GetValue<string>(),
                    "Auth__Audience",
                    StringComparison.Ordinal
                )
            )
            .NotNull()["Value"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("""{"Ref":"UserPoolClientId"}""");
    }

    [Test]
    public void Production_keeps_its_user_pool()
    {
        var template = Build();
        var pool = (JsonObject)template["Resources"].NotNull()["UserPool"].NotNull();
        var group = (JsonObject)template["Resources"].NotNull()["OperatorGroup"].NotNull();

        pool["DeletionPolicy"].NotNull().GetValue<string>().Should().Be("Retain");
        pool["UpdateReplacePolicy"].NotNull().GetValue<string>().Should().Be("Retain");
        pool["Properties"]
            .NotNull()["DeletionProtection"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("ACTIVE");
        group["DeletionPolicy"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("Retain", "deleting the group would drop every membership in the kept pool");
        // A pool a destroy kept is passed back in, and the stack creates none.
        template["Parameters"]
            .NotNull()[AwsStack.UserPoolParameter]
            .NotNull()["Default"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("");
        pool["Condition"].NotNull().GetValue<string>().Should().Be("CreateUserPool");
        group["Condition"].NotNull().GetValue<string>().Should().Be("CreateUserPool");
        Properties(template, "UserPoolClient")["UserPoolId"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Contain(AwsStack.UserPoolParameter);
        template["Outputs"]
            .NotNull()["UserPoolId"]
            .NotNull()["Value"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Contain(AwsStack.UserPoolParameter);
    }

    [Test]
    public void A_try_environment_keeps_no_user_pool()
    {
        var template = Build(File_(DeployEnvironment.Try));
        var pool = (JsonObject)template["Resources"].NotNull()["UserPool"].NotNull();

        pool.ContainsKey("DeletionPolicy").Should().BeFalse();
        pool.ContainsKey("UpdateReplacePolicy").Should().BeFalse();
        pool.ContainsKey("Condition").Should().BeFalse();
        ((JsonObject)pool["Properties"].NotNull())
            .ContainsKey("DeletionProtection")
            .Should()
            .BeFalse();
        ((JsonObject)template["Parameters"].NotNull())
            .ContainsKey(AwsStack.UserPoolParameter)
            .Should()
            .BeFalse();
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
    public void Each_template_fits_CloudFormations_inline_limit()
    {
        var templates = Templates();

        AwsStack
            .Compact(templates.Foundation)
            .Length.Should()
            .BeLessThan(AwsDeployer.InlineTemplateLimit);
        AwsStack
            .Compact(templates.Runtime)
            .Length.Should()
            .BeLessThan(AwsDeployer.InlineTemplateLimit);
    }

    [TestCase("eu-prod")]
    [TestCase("Prod")]
    [TestCase("prod_eu")]
    public void An_environment_name_other_than_letters_and_digits_is_refused(string name)
    {
        var file = File_();
        file.Environments[name] = file.Environments["production"];

        var act = () => file.Environment(name);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{name}'*lower-case letters and digits only*");
    }

    [Test]
    public void An_environment_named_base_is_refused_so_no_runtime_is_named_like_a_foundation()
    {
        // App "orders-prod" with environment "base" would run as orders-prod-base, the foundation of app "orders"
        // in environment "prod".
        var file = File_();
        file.Environments["base"] = file.Environments["production"];

        var act = () => file.Environment("base");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'base'*another app's foundation*");
    }

    [Test]
    public void An_environment_name_of_letters_and_digits_is_accepted()
    {
        var file = File_();
        file.Environments["2eu"] = file.Environments["production"];

        file.Environment("2eu").Should().BeSameAs(file.Environments["production"]);
    }

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
        var lines = AwsPlanSummary.Lines(File_(), "production", Templates()).ToList();

        lines[0].Should().StartWith("orders-production-base (production, us-west-2) has ");
        lines
            .Should()
            .Contain(l =>
                l.StartsWith(
                    "orders-production (production, us-west-2) has ",
                    StringComparison.Ordinal
                )
            );
        lines.Should().Contain(l => l.Contains("NAT gateway"));
        lines[^1].Should().EndWith("a month");
    }

    [Test]
    public void The_foundation_holds_the_network_database_secrets_and_sign_in()
    {
        var templates = Templates();
        var foundation = (JsonObject)templates.Foundation["Resources"].NotNull();
        var runtime = (JsonObject)templates.Runtime["Resources"].NotNull();

        foreach (
            var id in new[]
            {
                "Vpc",
                "NatGateway",
                "Database",
                "DatabaseSecret",
                "DatabaseSecurityGroup",
                "DatabaseClientSecurityGroup",
                "UserPool",
                "UserPoolClient",
            }
        )
        {
            foundation.Should().ContainKey(id);
            runtime.Should().NotContainKey(id);
        }
        foreach (
            var id in new[] { "Cluster", "LoadBalancer", "HubService", "MigrateTaskDefinition" }
        )
        {
            runtime.Should().ContainKey(id);
            foundation.Should().NotContainKey(id);
        }
    }

    [Test]
    public void Only_the_database_client_group_reaches_the_database()
    {
        var templates = Templates();
        var foundation = templates.Foundation;
        var runtime = templates.Runtime;

        var ingress = Resources(foundation, "AWS::EC2::SecurityGroupIngress")
            .Where(r =>
                string.Equals(
                    r.Resource["Properties"].NotNull()["GroupId"].NotNull().ToJsonString(),
                    """{"Ref":"DatabaseSecurityGroup"}""",
                    StringComparison.Ordinal
                )
            )
            .ToList();
        ingress.Should().ContainSingle();
        ingress[0]
            .Resource["Properties"]
            .NotNull()["SourceSecurityGroupId"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("""{"Ref":"DatabaseClientSecurityGroup"}""");
        foreach (var (id, service) in Resources(runtime, "AWS::ECS::Service"))
            service["Properties"]
                .NotNull()["NetworkConfiguration"]
                .NotNull()["AwsvpcConfiguration"]
                .NotNull()["SecurityGroups"]
                .NotNull()
                .ToJsonString()
                .Should()
                .Be(
                    """[{"Ref":"TaskSecurityGroup"},{"Ref":"DatabaseClientSecurityGroup"}]""",
                    $"{id} reaches the database through the foundation's client group"
                );
        Properties(runtime, "TaskSecurityGroup")["SecurityGroupEgress"]
            .NotNull()
            .AsArray()
            .Should()
            .NotContain(r => (int?)r.NotNull()["FromPort"] == 5432);
    }

    [Test]
    public void The_runtime_reads_the_foundation_by_parameter_never_by_import()
    {
        var templates = Templates();
        var foundationOutputs = (JsonObject)templates.Foundation["Outputs"].NotNull();
        var runtimeParameters = (JsonObject)templates.Runtime["Parameters"].NotNull();

        AwsStack.Compact(templates.Foundation).Should().NotContain("Fn::ImportValue");
        AwsStack.Compact(templates.Runtime).Should().NotContain("Fn::ImportValue");
        var read = runtimeParameters
            .Select(p => p.Key)
            .Where(AwsStack.FoundationValues.Contains)
            .ToList();
        read.Should()
            .Contain([
                "VpcId",
                "VpcCidr",
                "PublicSubnets",
                "ServiceSubnets",
                "DatabaseClientSecurityGroup",
                "DatabaseAddress",
                "DatabasePort",
                "DatabaseSecretArn",
                "UserPoolId",
                "UserPoolClientId",
            ]);
        foreach (var name in read)
            foundationOutputs.Should().ContainKey(name);
    }

    [Test]
    public void The_foundation_names_nothing_the_runtime_creates()
    {
        var templates = Templates();
        var foundation = AwsStack.Compact(templates.Foundation);

        foreach (var (id, _) in (JsonObject)templates.Runtime["Resources"].NotNull())
            foundation.Should().NotContain($"\"{id}\"", "the foundation is created first");
    }

    [Test]
    public void The_app_secrets_and_the_database_secret_keep_the_environments_name()
    {
        var templates = Templates();

        Properties(templates.Foundation, "DatabaseSecret")["Name"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("orders-production/database");
        AwsStack.Compact(templates.Runtime).Should().Contain("secret:orders-production/app/*");
    }

    [Test]
    public void An_existing_database_is_reached_through_the_client_group()
    {
        var file = File_();
        file.Environments["production"].DatabaseSecretArn =
            "arn:aws:secretsmanager:us-west-2:123:secret:orders-db";

        var templates = Templates(file);

        Resources(templates.Foundation, "AWS::RDS::DBInstance").Should().BeEmpty();
        Properties(templates.Foundation, "DatabaseClientSecurityGroup")["SecurityGroupEgress"]
            .NotNull()
            .AsArray()
            .Should()
            .Contain(r =>
                (int?)r.NotNull()["FromPort"] == 5432
                && string.Equals((string?)r["CidrIp"], "0.0.0.0/0", StringComparison.Ordinal)
            );
        ((JsonObject)templates.Runtime["Parameters"].NotNull())
            .Should()
            .NotContainKey("DatabaseAddress");
    }

    [Test]
    public void A_production_database_refuses_deletion_until_destroy_turns_it_off()
    {
        var foundation = Templates().Foundation;
        var protect = (JsonObject)
            foundation["Parameters"].NotNull()[AwsStack.ProtectParameter].NotNull();

        Properties(foundation, "Database")["DeletionProtection"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("""{"Fn::If":["Protected",true,false]}""");
        protect["Default"].NotNull().GetValue<string>().Should().Be("true");
        foundation["Conditions"]
            .NotNull()["Protected"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be($$"""{"Fn::Equals":[{"Ref":"{{AwsStack.ProtectParameter}}"},"true"]}""");
    }

    [Test]
    public void A_try_database_and_an_existing_database_have_no_protection_to_turn_off()
    {
        var tried = Templates(File_(DeployEnvironment.Try)).Foundation;
        var existing = File_();
        existing.Environments["production"].DatabaseSecretArn =
            "arn:aws:secretsmanager:us-west-2:123:secret:orders-db";

        ((bool)Properties(tried, "Database")["DeletionProtection"].NotNull()).Should().BeFalse();
        ((JsonObject?)tried["Parameters"] ?? []).Should().NotContainKey(AwsStack.ProtectParameter);
        ((JsonObject?)Templates(existing).Foundation["Parameters"] ?? [])
            .Should()
            .NotContainKey(AwsStack.ProtectParameter);
    }

    private static string OutputOf(JsonObject template, string name) =>
        template["Outputs"].NotNull()[name].NotNull()["Value"].NotNull().ToJsonString();

    [Test]
    public void Each_stack_lists_the_groups_it_creates_for_destroys_network_check()
    {
        var templates = Templates();
        var existing = File_();
        existing.Environments["production"].DatabaseSecretArn =
            "arn:aws:secretsmanager:us-west-2:123:secret:orders-db";

        OutputOf(templates.Foundation, "SecurityGroups")
            .Should()
            .Be(
                """{"Fn::Join":[",",[{"Ref":"DatabaseClientSecurityGroup"},{"Ref":"DatabaseSecurityGroup"}]]}"""
            );
        OutputOf(Templates(existing).Foundation, "SecurityGroups")
            .Should()
            .Be("""{"Fn::Join":[",",[{"Ref":"DatabaseClientSecurityGroup"}]]}""");
        OutputOf(templates.Runtime, "SecurityGroups")
            .Should()
            .Be(
                """{"Fn::Join":[",",[{"Ref":"TaskSecurityGroup"},{"Ref":"LoadBalancerSecurityGroup"}]]}"""
            );
    }

    [Test]
    public void The_migrate_task_wears_the_database_client_group()
    {
        OutputOf(Templates().Runtime, "TaskSecurityGroups")
            .Should()
            .Be(
                """{"Fn::Join":[",",[{"Ref":"TaskSecurityGroup"},{"Ref":"DatabaseClientSecurityGroup"}]]}"""
            );
    }
}
