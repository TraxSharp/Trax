using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy status</c>, <c>logs</c> and <c>destroy</c> on a kubernetes environment reach the cluster through
/// the same subcommands aws uses. Destroy deletes nothing until the namespace's name is typed back, or with
/// <c>--yes</c>, and never in a namespace another app owns; logs reads only the app's own services.
/// </summary>
public class DeployKubernetesCommandsTests
{
    private string _dir = "";
    private string _path = "";
    private KubernetesDeployerTests.FakeCluster _cluster = new();

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-k8s-commands-{Guid.NewGuid():N}");
        var file = DeployKubernetesTests.File_();
        foreach (var service in file.Services.Values)
        {
            var project = Path.Combine(_dir, service.Project);
            Directory.CreateDirectory(Path.GetDirectoryName(project) ?? _dir);
            await File.WriteAllTextAsync(project, "<Project />").ConfigureAwait(false);
        }
        file.Environments["local"] = new DeployEnvironment { Target = DeployEnvironment.Compose };
        _path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(_path, file.Write()).ConfigureAwait(false);
        _cluster = new KubernetesDeployerTests.FakeCluster
        {
            Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesTarget.AppLabel] = "orders",
            },
        };
        _cluster.Existing.Add(new KubernetesObjectState("Deployment", "hub", "orders"));
        DeployKubernetesCommands.Client = () => _cluster;
    }

    [TearDown]
    public void TearDown()
    {
        DeployKubernetesCommands.Client = () =>
            new KubernetesDeployClient(DeployKubernetesCommands.Clock);
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private async Task<(int Exit, string Out, string Error)> DestroyAsync(
        Func<string?> readLine,
        bool yes = false
    )
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await DeployKubernetesCommands
            .DestroyAsync(
                _path,
                "staging",
                deleteData: true,
                yes,
                readLine,
                stdout,
                stderr,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private List<string> Deleted() =>
        [.. _cluster.Calls.Where(c => c.StartsWith("delete", StringComparison.Ordinal))];

    [TestCase("orders")]
    [TestCase("")]
    [TestCase(null)]
    public async Task Destroy_deletes_nothing_unless_the_namespace_is_typed_back(string? typed)
    {
        var (exit, output, error) = await DestroyAsync(() => typed);

        exit.Should().Be(1);
        output
            .Should()
            .Contain("in-cluster database with every row")
            .And.EndWith("Type orders-staging to go on: ");
        error.Should().Contain("Nothing was deleted.");
        _cluster.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task Destroy_deletes_the_apps_objects_once_the_namespace_is_typed_back()
    {
        var (exit, _, _) = await DestroyAsync(() => " orders-staging ");

        exit.Should().Be(0);
        Deleted().Should().Equal("delete Deployment/hub");
    }

    [Test]
    public async Task Destroy_with_yes_asks_nothing()
    {
        var (exit, output, _) = await DestroyAsync(
            () => throw new InvalidOperationException("asked"),
            yes: true
        );

        exit.Should().Be(0);
        output.Should().NotContain("Type orders-staging");
        Deleted().Should().Equal("delete Deployment/hub");
    }

    [Test]
    public async Task Destroy_refuses_a_namespace_another_app_owns_and_deletes_nothing()
    {
        _cluster.Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesTarget.AppLabel] = "billing",
        };

        var (exit, _, error) = await DestroyAsync(() => null, yes: true);

        exit.Should().Be(1);
        error.Should().Contain("Namespace orders-staging belongs to app 'billing'");
        Deleted().Should().BeEmpty();
    }

    [Test]
    public async Task Destroy_through_the_deploy_command_reaches_the_cluster()
    {
        (
            await DeployAwsCommands.DestroyAsync(
                _path,
                "staging",
                deleteData: false,
                yes: true,
                TextWriter.Null,
                TextWriter.Null,
                CancellationToken.None
            )
        )
            .Should()
            .Be(0);
        Deleted().Should().Equal("delete Deployment/hub");
    }

    [Test]
    public async Task Status_lists_each_workload_with_its_image()
    {
        _cluster.Workloads.Add(
            new KubernetesWorkloadState("Deployment", "hub", 1, 1, "orders-hub:1")
        );
        _cluster.Workloads.Add(
            new KubernetesWorkloadState("Deployment", "worker", 1, 3, "orders-worker:1")
        );
        using var stdout = new StringWriter();

        var exit = await DeployAwsCommands.StatusAsync(
            _path,
            "staging",
            stdout,
            TextWriter.Null,
            CancellationToken.None
        );

        exit.Should().Be(0);
        stdout
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Should()
            .Equal(
                "orders-staging in context kind-test:",
                "  hub             1/1 ready  orders-hub:1",
                "  worker          1/3 ready  orders-worker:1"
            );
        _cluster.Calls.Should().Contain("workloads orders");
    }

    [Test]
    public async Task Status_of_a_namespace_that_does_not_exist_says_so()
    {
        _cluster.Namespace = null;
        using var stdout = new StringWriter();

        var exit = await DeployAwsCommands.StatusAsync(
            _path,
            "staging",
            stdout,
            TextWriter.Null,
            CancellationToken.None
        );

        exit.Should().Be(0);
        stdout
            .ToString()
            .Should()
            .Contain("Namespace orders-staging does not exist in context kind-test.");
        _cluster
            .Calls.Should()
            .NotContain(c => c.StartsWith("workloads", StringComparison.Ordinal));
    }

    [TestCase("worker")]
    [TestCase("migrate")]
    [TestCase("database")]
    [TestCase("rabbitmq")]
    public async Task Logs_streams_a_service_of_the_app(string service)
    {
        _cluster.LogLines.AddRange(["[worker-a] started", "[worker-a] ready"]);
        using var stdout = new StringWriter();

        var exit = await DeployAwsCommands.LogsAsync(
            _path,
            "staging",
            service,
            follow: false,
            sinceMinutes: 60,
            stdout,
            TextWriter.Null,
            CancellationToken.None
        );

        exit.Should().Be(0);
        stdout.ToString().Should().Contain("[worker-a] started").And.Contain("[worker-a] ready");
        _cluster.Calls.Should().Equal($"logs {service}");
    }

    [TestCase("nope")]
    [TestCase("hub,trax/app=billing")]
    public async Task Logs_refuses_a_name_that_is_not_a_service_before_reaching_the_cluster(
        string service
    )
    {
        using var stderr = new StringWriter();

        var exit = await DeployAwsCommands.LogsAsync(
            _path,
            "staging",
            service,
            follow: false,
            sinceMinutes: 60,
            TextWriter.Null,
            stderr,
            CancellationToken.None
        );

        exit.Should().Be(1);
        stderr
            .ToString()
            .Should()
            .Contain($"'{service}' is not a service. Read one of: hub, reports, worker, migrate");
        _cluster.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task Status_of_a_compose_environment_points_at_deploy()
    {
        using var stderr = new StringWriter();

        var exit = await DeployAwsCommands.StatusAsync(
            _path,
            "local",
            TextWriter.Null,
            stderr,
            CancellationToken.None
        );

        exit.Should().Be(1);
        stderr.ToString().Should().Contain("'local' is a compose one");
        _cluster.Calls.Should().BeEmpty();
    }
}
