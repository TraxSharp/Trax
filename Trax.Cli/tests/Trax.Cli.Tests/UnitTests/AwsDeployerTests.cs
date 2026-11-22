using System.Reflection;
using System.Runtime.CompilerServices;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy --env</c> checks the app's secrets before creating anything, pushes each image under a new tag,
/// changes the stack in two passes around the migration (a new stack's services wait at zero; an existing stack's
/// keep their image until the migration succeeds), and moves nothing when the migration fails. Destroy unprotects a
/// production database first.
/// </summary>
public class AwsDeployerTests
{
    private const string Tag = "20261020-120000-abc";

    private static (AwsDeployer Deployer, FakeAws Aws, FakeImages Images) Deployer()
    {
        var aws = new FakeAws();
        var images = new FakeImages();
        return (new AwsDeployer(aws, images, _ => { }), aws, images);
    }

    private static Task<string?> DeployAsync(AwsDeployer deployer) =>
        deployer.DeployAsync(
            AwsStackTemplateTests.File_(),
            "/src/trax.deploy.json",
            "production",
            AwsStackTemplateTests.Exports(),
            Tag,
            CancellationToken.None
        );

    [Test]
    public async Task A_new_stack_starts_its_services_only_after_the_migration()
    {
        var (deployer, aws, images) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");

        var url = await DeployAsync(deployer);

        aws.Calls.Should().Equal("deploy", "run-task", "deploy");
        aws.Deploys[0]["HubCount"].Should().Be("0");
        aws.Deploys[0]["WorkerCount"].Should().Be("0");
        aws.Deploys[0]["HubImage"].Should().Be($"repo/trax/orders-production/hub:{Tag}");
        aws.Deploys[1]["HubCount"].Should().Be("1");
        aws.Deploys[1]["WorkerCount"].Should().Be("3");
        aws.Deploys[1]["WorkerImage"].Should().Be($"repo/trax/orders-production/worker:{Tag}");
        aws.Task.NotNull().TaskDefinition.Should().Be("migrate-td");
        images.Published.Should().Equal("hub", "worker");
        url.Should().Be("https://orders.example.com");
    }

    [Test]
    public async Task An_existing_stack_keeps_its_services_on_their_image_until_the_migration_succeeds()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running();

        await DeployAsync(deployer);

        aws.Deploys[0]["HubCount"].Should().BeNull("null keeps the previous value");
        aws.Deploys[0]["HubImage"].Should().BeNull("null keeps the previous value");
        aws.Deploys[0][AwsStack.MigrateImageParameter]
            .Should()
            .Be($"repo/trax/orders-production/hub:{Tag}");
        aws.Deploys[1]["HubImage"].Should().Be($"repo/trax/orders-production/hub:{Tag}");
    }

    [Test]
    public async Task A_new_service_stays_at_zero_until_the_migration_ran()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running();
        var file = AwsStackTemplateTests.File_();
        file.Services["reports"] = new DeployService
        {
            Project = "Orders.Reports/Orders.Reports.csproj",
            Count = 2,
        };
        var exports = AwsStackTemplateTests.Exports();
        exports["reports"] = new DeployManifest
        {
            Roles = ["worker"],
            Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
        };

        await deployer.DeployAsync(
            file,
            "/src/trax.deploy.json",
            "production",
            exports,
            Tag,
            CancellationToken.None
        );

        aws.Calls.Should().Equal("deploy", "run-task", "deploy");
        aws.Deploys[0]["ReportsCount"].Should().Be("0", "its image is new and the schema is not");
        aws.Deploys[0]["HubCount"].Should().BeNull("a running service keeps its count");
        aws.Deploys[0]["HubImage"].Should().BeNull("and its image");
        aws.Deploys[1]["ReportsCount"].Should().Be("2");
        aws.Deploys[1]["WorkerCount"]
            .Should()
            .Be("3", "the environment's count overrides the file's");
        aws.Deploys[1]["ReportsImage"].Should().Be($"repo/trax/orders-production/reports:{Tag}");
    }

    [Test]
    public async Task A_stack_from_before_service_counts_keeps_its_services_running()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running() with
        {
            Parameters = new Dictionary<string, string>
            {
                ["Scale"] = "1",
                [AwsStack.MigrateImageParameter] = "old",
                ["HubImage"] = "old",
                ["WorkerImage"] = "old",
                [AwsStack.ProtectParameter] = "true",
            },
        };

        await DeployAsync(deployer);

        aws.Deploys[0]["HubCount"].Should().Be("1");
        aws.Deploys[0]["WorkerCount"].Should().Be("3");
        aws.Deploys[0]["HubImage"].Should().BeNull();
        aws.Deploys[0].Should().NotContainKey("Scale", "the template no longer declares it");
    }

    [Test]
    public async Task Refuses_a_stack_another_app_owns()
    {
        var (deployer, aws, images) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running() with
        {
            Tags = new Dictionary<string, string>
            {
                ["trax:app"] = "orders-eu",
                ["trax:environment"] = "production",
            },
        };

        var act = () => DeployAsync(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*orders-production*app 'orders-eu'*not 'orders'*");
        aws.Calls.Should().BeEmpty();
        images.Published.Should().BeEmpty();
    }

    [Test]
    public async Task A_failed_migration_moves_no_service()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running();
        aws.ExitCode = 1;

        var act = () => DeployAsync(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*exit code 1*trax deploy logs migrate*");
        aws.Calls.Should().Equal("deploy", "run-task");
    }

    [Test]
    public async Task A_secret_with_no_value_stops_the_deploy_before_anything_is_created()
    {
        var (deployer, aws, images) = Deployer();

        var act = () => DeployAsync(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no value for Stripe__ApiKey*secret set*");
        aws.Calls.Should().BeEmpty();
        images.Published.Should().BeEmpty();
    }

    [Test]
    public async Task A_stack_whose_creation_failed_must_be_destroyed_first()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = new AwsStackState(
            "ROLLBACK_COMPLETE",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
        )
        {
            Tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["trax:app"] = "orders",
            },
        };

        var act = () => DeployAsync(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ROLLBACK_COMPLETE*destroy*");
    }

    [Test]
    public async Task A_stack_an_abandoned_first_deploy_left_in_review_is_created_again()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = new AwsStackState(
            "REVIEW_IN_PROGRESS",
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        await DeployAsync(deployer);

        aws.Calls.Should().Equal("deploy", "run-task", "deploy");
        aws.Deploys[0]["HubCount"].Should().Be("0");
        aws.Deploys[0]["HubImage"].Should().Be($"repo/trax/orders-production/hub:{Tag}");
    }

    [TestCase(null, "CREATE")]
    [TestCase("REVIEW_IN_PROGRESS", "CREATE")]
    [TestCase("UPDATE_COMPLETE", "UPDATE")]
    public void A_change_set_creates_a_stack_that_holds_nothing_yet(string? status, string type) =>
        AwsDeployClient
            .ChangeSetTypeFor(
                status is null
                    ? null
                    : new AwsStackState(
                        status,
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>()
                    )
            )
            .Value.Should()
            .Be(type);

    [Test]
    public async Task Destroy_unprotects_a_production_database_then_deletes_the_stack()
    {
        var (deployer, aws, _) = Deployer();
        aws.Existing = FakeAws.Running();

        await deployer.DestroyAsync(
            AwsStackTemplateTests.File_(),
            "production",
            deleteData: true,
            CancellationToken.None
        );

        aws.Calls.Should()
            .Equal(
                "deploy",
                "delete-stack",
                "delete-user-pool",
                "delete-parameter",
                "delete-repository",
                "delete-repository",
                "delete-secret"
            );
        aws.Deploys[0][AwsStack.ProtectParameter].Should().Be("false");
        aws.Templates[0].Should().BeNull("destroy keeps the template the stack has");
    }

    [Test]
    public async Task Destroy_keeps_a_production_user_pool_and_records_it()
    {
        var said = new List<string>();
        var aws = new FakeAws { Existing = FakeAws.Running() };
        aws.Pools["us-west-2_pool1"] = 42;
        var deployer = new AwsDeployer(aws, new FakeImages(), said.Add);

        await deployer.DestroyAsync(
            AwsStackTemplateTests.File_(),
            "production",
            deleteData: false,
            CancellationToken.None
        );

        aws.Parameters.Should()
            .Equal(
                new Dictionary<string, string>
                {
                    [AwsDeployer.UserPoolRecord("orders-production")] = "us-west-2_pool1",
                }
            );
        aws.Pools.Should().ContainKey("us-west-2_pool1");
        aws.Calls.Should().NotContain("delete-user-pool");
        said.Should().Contain(l => l.Contains("us-west-2_pool1") && l.Contains("42 users"));
    }

    [Test]
    public async Task A_deploy_after_destroy_signs_in_against_the_kept_pool()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Parameters[AwsDeployer.UserPoolRecord("orders-production")] = "us-west-2_pool1";

        await DeployAsync(deployer);

        aws.Deploys.Should()
            .AllSatisfy(d => d[AwsStack.UserPoolParameter].Should().Be("us-west-2_pool1"));
    }

    [Test]
    public async Task A_first_deploy_creates_its_user_pool()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");

        await DeployAsync(deployer);

        aws.Deploys.Should().AllSatisfy(d => d[AwsStack.UserPoolParameter].Should().Be(""));
    }

    [Test]
    public async Task Destroy_with_delete_data_deletes_the_user_pool_and_its_record()
    {
        var (deployer, aws, _) = Deployer();
        aws.Existing = FakeAws.Running();
        aws.Pools["us-west-2_pool1"] = 42;
        aws.Parameters[AwsDeployer.UserPoolRecord("orders-production")] = "us-west-2_pool1";

        await deployer.DestroyAsync(
            AwsStackTemplateTests.File_(),
            "production",
            deleteData: true,
            CancellationToken.None
        );

        aws.Pools.Should().BeEmpty();
        aws.Parameters.Should().BeEmpty();
    }

    [Test]
    public async Task Destroy_with_delete_data_deletes_a_pool_an_earlier_destroy_kept()
    {
        var (deployer, aws, _) = Deployer();
        aws.Pools["us-west-2_pool1"] = 42;
        aws.Parameters[AwsDeployer.UserPoolRecord("orders-production")] = "us-west-2_pool1";

        await deployer.DestroyAsync(
            AwsStackTemplateTests.File_(),
            "production",
            deleteData: true,
            CancellationToken.None
        );

        aws.Pools.Should().BeEmpty();
        aws.Parameters.Should().BeEmpty();
    }

    [TestCase(
        DeployEnvironment.Production,
        false,
        "This deletes orders-production. Its user pool and every user in it are kept, and the next deploy signs in "
            + "against them; --delete-data deletes them. Type orders-production to go on: "
    )]
    [TestCase(
        DeployEnvironment.Production,
        true,
        "This deletes orders-production, its user pool with every user in it, its images and its secrets. Type "
            + "orders-production to go on: "
    )]
    [TestCase(
        DeployEnvironment.Try,
        false,
        "This deletes orders-production, its user pool with every user in it. Type orders-production to go on: "
    )]
    public void Destroy_says_what_happens_to_the_users(
        string profile,
        bool deleteData,
        string prompt
    ) =>
        DeployAwsCommands
            .DestroyPrompt(AwsStackTemplateTests.File_(profile), "production", deleteData)
            .Should()
            .Be(prompt);

    [Test]
    public async Task Secret_set_refuses_a_name_the_file_does_not_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Orders"));
        Directory.CreateDirectory(Path.Combine(dir, "Orders.Worker"));
        await File.WriteAllTextAsync(Path.Combine(dir, "Orders/Orders.csproj"), "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(dir, "Orders.Worker/Orders.Worker.csproj"),
            "<Project />"
        );
        var path = Path.Combine(dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, AwsStackTemplateTests.File_().Write());
        var aws = new FakeAws();
        DeployAwsCommands.Client = _ => aws;
        try
        {
            (
                await DeployAwsCommands.SecretSetAsync(
                    path,
                    "production",
                    "Other__Key",
                    () => Task.FromResult("x"),
                    TextWriter.Null,
                    TextWriter.Null,
                    CancellationToken.None
                )
            )
                .Should()
                .Be(1);
            (
                await DeployAwsCommands.SecretSetAsync(
                    path,
                    "production",
                    "Stripe__ApiKey",
                    () => Task.FromResult("sk_live"),
                    TextWriter.Null,
                    TextWriter.Null,
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
            aws.Secrets.Should().Contain("orders-production/app/Stripe__ApiKey");
        }
        finally
        {
            DeployAwsCommands.Client = region => new AwsDeployClient(
                region,
                DeployAwsCommands.Clock
            );
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void An_image_is_pushed_for_linux_x64_with_the_registry_login_in_its_environment()
    {
        var psi = DotnetImagePublisher.StartInfo(
            "/src/Orders/Orders.csproj",
            "123.dkr.ecr.us-west-2.amazonaws.com/trax/orders-production/hub",
            Tag,
            new RegistryLogin("123.dkr.ecr.us-west-2.amazonaws.com", "AWS", "token")
        );

        psi.ArgumentList.Should()
            .Contain([
                "-t:PublishContainer",
                "-p:ContainerRegistry=123.dkr.ecr.us-west-2.amazonaws.com",
                "-p:ContainerRepository=trax/orders-production/hub",
                $"-p:ContainerImageTags={Tag}",
                "linux",
                "x64",
            ]);
        psi.Environment["DOTNET_CONTAINER_REGISTRY_UNAME"].Should().Be("AWS");
        psi.Environment["DOTNET_CONTAINER_REGISTRY_PWORD"].Should().Be("token");
        string.Join(' ', psi.ArgumentList)
            .Should()
            .NotContain("token", "the password stays out of the command line");
    }

    [Test]
    public async Task An_image_tag_starts_with_the_time() =>
        (
            await AwsDeployer
                .ImageTagAsync(
                    Path.GetTempPath(),
                    new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)
                )
                .ConfigureAwait(false)
        )
            .Should()
            .StartWith("20261020-120000");

    [TestCase("AWSSDK.SSO.dll")]
    [TestCase("AWSSDK.SSOOIDC.dll")]
    public void The_tool_ships_what_the_AWS_SDK_signs_in_to_an_SSO_profile_with(string assembly) =>
        File.Exists(
                Path.Combine(
                    Path.GetDirectoryName(typeof(AwsDeployClient).Assembly.Location)!,
                    assembly
                )
            )
            .Should()
            .BeTrue("the SDK loads it by reflection for a profile signed in with aws sso login");

    [Test]
    public void A_hidden_secret_keeps_only_the_characters_typed()
    {
        var keys = new Queue<ConsoleKeyInfo>([
            Key('a', ConsoleKey.A),
            Key('\0', ConsoleKey.LeftArrow),
            Key('b', ConsoleKey.B),
            Key('\t', ConsoleKey.Tab),
            Key('\0', ConsoleKey.Delete),
            Key('c', ConsoleKey.C),
            Key('\b', ConsoleKey.Backspace),
            Key('d', ConsoleKey.D),
            Key('\r', ConsoleKey.Enter),
        ]);

        DeployAwsCommands.ReadHidden(keys.Dequeue).Should().Be("abd");
    }

    private static ConsoleKeyInfo Key(char c, ConsoleKey key) => new(c, key, false, false, false);

    [Test]
    public async Task Setting_a_secret_that_destroy_deleted_restores_it_first()
    {
        var secrets = DispatchProxy.Create<IAmazonSecretsManager, FakeSecretsManager>();
        var fake = (FakeSecretsManager)(object)secrets;
        fake.PendingDeletion = true;

        await AwsDeployClient.PutSecretAsync(
            secrets,
            "orders-staging/app/Stripe__ApiKey",
            "sk_test",
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        fake.Calls.Should().Equal("PutSecretValue", "RestoreSecret", "PutSecretValue");
        fake.Value.Should().Be("sk_test");
    }

    [Test]
    public async Task Setting_a_secret_that_does_not_exist_creates_it()
    {
        var secrets = DispatchProxy.Create<IAmazonSecretsManager, FakeSecretsManager>();
        var fake = (FakeSecretsManager)(object)secrets;
        fake.Missing = true;

        await AwsDeployClient.PutSecretAsync(
            secrets,
            "orders-staging/app/Stripe__ApiKey",
            "sk_test",
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        fake.Calls.Should().Equal("PutSecretValue", "CreateSecret");
        fake.Value.Should().Be("sk_test");
    }

    [Test]
    public void Following_a_log_keeps_only_the_ids_at_its_newest_time()
    {
        var seen = new SeenLogEvents();

        for (var i = 0; i < 10_000; i++)
            seen.Add($"e{i}", 1_000 + i).Should().BeTrue();

        seen.Count.Should().Be(1, "only an event at the newest time can be read again");
        seen.Add("e9999", 10_999).Should().BeFalse("it was printed already");
        seen.Add("other", 10_999).Should().BeTrue();
        seen.Add("late", 10_000)
            .Should()
            .BeTrue("the next read starts after it, so it cannot repeat");
    }

    [TestCase(typeof(UnauthorizedAccessException))]
    [TestCase(typeof(System.Text.Json.JsonException))]
    [TestCase(typeof(System.ComponentModel.Win32Exception))]
    public async Task A_deploy_that_fails_reports_it_in_a_line(Type failure)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Orders"));
        Directory.CreateDirectory(Path.Combine(dir, "Orders.Worker"));
        await File.WriteAllTextAsync(Path.Combine(dir, "Orders/Orders.csproj"), "<Project />")
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(dir, "Orders.Worker/Orders.Worker.csproj"),
                "<Project />"
            )
            .ConfigureAwait(false);
        var path = Path.Combine(dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, AwsStackTemplateTests.File_().Write())
            .ConfigureAwait(false);
        try
        {
            (
                await DeployAwsCommands
                    .DeployAsync(
                        path,
                        "production",
                        new ThrowingExporter(
                            (Exception)Activator.CreateInstance(failure).NotNull()
                        ),
                        TextWriter.Null,
                        TextWriter.Null,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false)
            )
                .Should()
                .Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    internal sealed class ThrowingExporter(Exception failure) : IHostExporter
    {
        public Task<DeployManifest> ExportAsync(
            string projectPath,
            IReadOnlyDictionary<string, string> environment
        ) => Task.FromException<DeployManifest>(failure);
    }

    /// <summary>
    /// Secrets Manager, holding one secret that is missing or pending deletion, as the service answers for each.
    /// </summary>
    public class FakeSecretsManager : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public bool PendingDeletion { get; set; }
        public bool Missing { get; set; }
        public string? Value { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name == "Dispose")
                return null;
            Calls.Add(name.Replace("Async", "", StringComparison.Ordinal));
            switch (name)
            {
                case "PutSecretValueAsync":
                    if (PendingDeletion)
                        return System.Threading.Tasks.Task.FromException<PutSecretValueResponse>(
                            new InvalidRequestException(
                                "You can't perform this operation on the secret because it was marked for deletion."
                            )
                        );
                    if (Missing)
                        return System.Threading.Tasks.Task.FromException<PutSecretValueResponse>(
                            new ResourceNotFoundException(
                                "Secrets Manager can't find the specified secret."
                            )
                        );
                    Value = ((PutSecretValueRequest)args![0]!).SecretString;
                    return System.Threading.Tasks.Task.FromResult(new PutSecretValueResponse());
                case "RestoreSecretAsync":
                    PendingDeletion = false;
                    return System.Threading.Tasks.Task.FromResult(new RestoreSecretResponse());
                case "CreateSecretAsync":
                    Value = ((CreateSecretRequest)args![0]!).SecretString;
                    return System.Threading.Tasks.Task.FromResult(new CreateSecretResponse());
                default:
                    throw new NotSupportedException(name);
            }
        }
    }

    internal sealed class FakeImages : IImagePublisher
    {
        public List<string> Published { get; } = [];

        public Task PublishAsync(
            string projectPath,
            string repositoryUri,
            string tag,
            RegistryLogin login
        )
        {
            Published.Add(repositoryUri[(repositoryUri.LastIndexOf('/') + 1)..]);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeAws : IAwsDeployClient
    {
        public List<string> Calls { get; } = [];
        public List<IReadOnlyDictionary<string, string?>> Deploys { get; } = [];
        public List<string?> Templates { get; } = [];
        public HashSet<string> Secrets { get; } = [];
        public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);

        /// <summary>The user pools that exist, with how many users each holds.</summary>
        public Dictionary<string, long> Pools { get; } = new(StringComparer.Ordinal);
        public AwsStackState? Existing { get; set; }
        public int? ExitCode { get; set; } = 0;
        public AwsTaskRequest? Task { get; private set; }

        public static AwsStackState Running() =>
            new(
                "UPDATE_COMPLETE",
                Outputs(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["HubCount"] = "1",
                    ["WorkerCount"] = "3",
                    [AwsStack.MigrateImageParameter] = "old",
                    ["HubImage"] = "old",
                    ["WorkerImage"] = "old",
                    [AwsStack.ProtectParameter] = "true",
                }
            )
            {
                Tags = new Dictionary<string, string>
                {
                    ["trax:app"] = "orders",
                    ["trax:environment"] = "production",
                },
            };

        private static Dictionary<string, string> Outputs() =>
            new(StringComparer.Ordinal)
            {
                ["ClusterName"] = "orders-production",
                ["MigrateTaskDefinition"] = "migrate-td",
                ["ServiceSubnets"] = "subnet-1,subnet-2",
                ["TaskSecurityGroup"] = "sg-1",
                ["AssignPublicIp"] = "DISABLED",
                ["Url"] = "https://orders.example.com",
                ["UserPoolId"] = "us-west-2_pool1",
            };

        public string Region => "us-west-2";

        public Task<string> AccountIdAsync(CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult("123");

        public Task<string?> CloudFrontPrefixListAsync(CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<string?>("pl-1");

        public Task<string> EnsureRepositoryAsync(
            string name,
            IReadOnlyDictionary<string, string> tags,
            CancellationToken ct
        ) => System.Threading.Tasks.Task.FromResult($"repo/{name}");

        public Task<RegistryLogin> RegistryLoginAsync(CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new RegistryLogin("repo", "AWS", "pw"));

        public Task<AwsStackState?> DescribeStackAsync(string stack, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(
                Calls.Contains("deploy") && Existing is null or { Created: false }
                    ? new AwsStackState(
                        "CREATE_COMPLETE",
                        Outputs(),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                    )
                    : Existing
            );

        public Task<bool> DeployAsync(
            string stack,
            string? template,
            IReadOnlyDictionary<string, string?> parameters,
            IReadOnlyDictionary<string, string> tags,
            Action<string> progress,
            CancellationToken ct
        )
        {
            Calls.Add("deploy");
            Deploys.Add(parameters);
            Templates.Add(template);
            return System.Threading.Tasks.Task.FromResult(true);
        }

        public Task<AwsTaskResult> RunTaskAsync(
            AwsTaskRequest request,
            Action<string> progress,
            CancellationToken ct
        )
        {
            Calls.Add("run-task");
            Task = request;
            return System.Threading.Tasks.Task.FromResult(
                new AwsTaskResult(ExitCode, StoppedReason: null, "migrate/migrate/1")
            );
        }

        public Task DeleteStackAsync(string stack, Action<string> progress, CancellationToken ct)
        {
            Calls.Add("delete-stack");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task DeleteRepositoryAsync(string name, CancellationToken ct)
        {
            Calls.Add("delete-repository");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task PutSecretAsync(
            string name,
            string value,
            IReadOnlyDictionary<string, string> tags,
            CancellationToken ct
        )
        {
            Secrets.Add(name);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string name, CancellationToken ct)
        {
            Calls.Add("delete-secret");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<IReadOnlyList<AwsSecret>> ListSecretsAsync(
            string prefix,
            CancellationToken ct
        ) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<AwsSecret>>([
                .. Secrets
                    .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(s => new AwsSecret(s, Changed: null)),
            ]);

        public Task<string?> GetParameterAsync(string name, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(Parameters.GetValueOrDefault(name));

        public Task PutParameterAsync(
            string name,
            string value,
            IReadOnlyDictionary<string, string> tags,
            CancellationToken ct
        )
        {
            Calls.Add("put-parameter");
            Parameters[name] = value;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task DeleteParameterAsync(string name, CancellationToken ct)
        {
            Calls.Add("delete-parameter");
            Parameters.Remove(name);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<long?> UserCountAsync(string userPoolId, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(
                Pools.TryGetValue(userPoolId, out var count) ? count : (long?)null
            );

        public Task DeleteUserPoolAsync(string userPoolId, CancellationToken ct)
        {
            Calls.Add("delete-user-pool");
            Pools.Remove(userPoolId);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<IReadOnlyList<AwsServiceState>> DescribeServicesAsync(
            string cluster,
            IReadOnlyList<string> services,
            CancellationToken ct
        ) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AwsServiceState>>([]);

        public async IAsyncEnumerable<string> LogsAsync(
            string group,
            DateTime since,
            bool follow,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
