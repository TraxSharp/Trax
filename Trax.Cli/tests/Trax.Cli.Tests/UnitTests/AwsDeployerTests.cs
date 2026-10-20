using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;

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

    private static Task<string?> Deploy(AwsDeployer deployer) =>
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

        var url = await Deploy(deployer);

        aws.Calls.Should().Equal("deploy", "run-task", "deploy");
        aws.Deploys[0][AwsStack.ScaleParameter].Should().Be("0");
        aws.Deploys[0]["HubImage"].Should().Be($"repo/trax/orders-production/hub:{Tag}");
        aws.Deploys[1][AwsStack.ScaleParameter].Should().Be("1");
        aws.Deploys[1]["WorkerImage"].Should().Be($"repo/trax/orders-production/worker:{Tag}");
        aws.Task!.TaskDefinition.Should().Be("migrate-td");
        images.Published.Should().Equal("hub", "worker");
        url.Should().Be("https://orders.example.com");
    }

    [Test]
    public async Task An_existing_stack_keeps_its_services_on_their_image_until_the_migration_succeeds()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running();

        await Deploy(deployer);

        aws.Deploys[0][AwsStack.ScaleParameter].Should().Be("1");
        aws.Deploys[0]["HubImage"].Should().BeNull("null keeps the previous value");
        aws.Deploys[0][AwsStack.MigrateImageParameter]
            .Should()
            .Be($"repo/trax/orders-production/hub:{Tag}");
        aws.Deploys[1]["HubImage"].Should().Be($"repo/trax/orders-production/hub:{Tag}");
    }

    [Test]
    public async Task A_failed_migration_moves_no_service()
    {
        var (deployer, aws, _) = Deployer();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        aws.Existing = FakeAws.Running();
        aws.ExitCode = 1;

        var act = () => Deploy(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*exit code 1*trax deploy logs migrate*");
        aws.Calls.Should().Equal("deploy", "run-task");
    }

    [Test]
    public async Task A_secret_with_no_value_stops_the_deploy_before_anything_is_created()
    {
        var (deployer, aws, images) = Deployer();

        var act = () => Deploy(deployer);

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
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var act = () => Deploy(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ROLLBACK_COMPLETE*destroy*");
    }

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
                "delete-repository",
                "delete-repository",
                "delete-secret"
            );
        aws.Deploys[0][AwsStack.ProtectParameter].Should().Be("false");
        aws.Templates[0].Should().BeNull("destroy keeps the template the stack has");
    }

    [Test]
    public async Task Secret_set_refuses_a_name_the_file_does_not_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Orders"));
        Directory.CreateDirectory(Path.Combine(dir, "Orders.Worker"));
        File.WriteAllText(Path.Combine(dir, "Orders/Orders.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(dir, "Orders.Worker/Orders.Worker.csproj"), "<Project />");
        var path = Path.Combine(dir, DeployFile.FileName);
        File.WriteAllText(path, AwsStackTemplateTests.File_().Write());
        var aws = new FakeAws();
        DeployAwsCommands.Client = _ => aws;
        try
        {
            (
                await DeployAwsCommands.SecretSetAsync(
                    path,
                    "production",
                    "Other__Key",
                    () => "x",
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
                    () => "sk_live",
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
            aws.Secrets.Should().Contain("orders-production/app/Stripe__ApiKey");
        }
        finally
        {
            DeployAwsCommands.Client = region => new AwsDeployClient(region);
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
    public void An_image_tag_starts_with_the_time() =>
        AwsDeployer
            .ImageTag(Path.GetTempPath(), new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc))
            .Should()
            .StartWith("20261020-120000");

    internal sealed class FakeImages : IImagePublisher
    {
        public List<string> Published { get; } = [];

        public void Publish(
            string projectPath,
            string repositoryUri,
            string tag,
            RegistryLogin login
        ) => Published.Add(repositoryUri[(repositoryUri.LastIndexOf('/') + 1)..]);
    }

    internal sealed class FakeAws : IAwsDeployClient
    {
        public List<string> Calls { get; } = [];
        public List<IReadOnlyDictionary<string, string?>> Deploys { get; } = [];
        public List<string?> Templates { get; } = [];
        public HashSet<string> Secrets { get; } = [];
        public AwsStackState? Existing { get; set; }
        public int? ExitCode { get; set; } = 0;
        public AwsTaskRequest? Task { get; private set; }

        public static AwsStackState Running() =>
            new(
                "UPDATE_COMPLETE",
                Outputs(),
                new Dictionary<string, string>
                {
                    [AwsStack.ScaleParameter] = "1",
                    [AwsStack.MigrateImageParameter] = "old",
                    ["HubImage"] = "old",
                    ["WorkerImage"] = "old",
                    [AwsStack.ProtectParameter] = "true",
                }
            );

        private static Dictionary<string, string> Outputs() =>
            new()
            {
                ["ClusterName"] = "orders-production",
                ["MigrateTaskDefinition"] = "migrate-td",
                ["ServiceSubnets"] = "subnet-1,subnet-2",
                ["TaskSecurityGroup"] = "sg-1",
                ["AssignPublicIp"] = "DISABLED",
                ["Url"] = "https://orders.example.com",
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
                Calls.Contains("deploy")
                    ? Existing
                        ?? new AwsStackState(
                            "CREATE_COMPLETE",
                            Outputs(),
                            new Dictionary<string, string>()
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
                new AwsTaskResult(ExitCode, null, "migrate/migrate/1")
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
                    .Select(s => new AwsSecret(s, null)),
            ]);

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
            await System.Threading.Tasks.Task.CompletedTask;
            yield break;
        }
    }
}
