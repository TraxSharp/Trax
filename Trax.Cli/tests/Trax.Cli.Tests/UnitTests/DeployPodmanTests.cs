using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A kubernetes environment with <c>"runtime": "podman"</c> becomes the same manifests in the subset
/// <c>podman kube play</c> runs, each pod under a Quadlet unit on the app's own network, the migration run by the
/// migrator's unit before its pod and every other service after it. Secret values come from the <c>.env</c> beside
/// trax.deploy.json into a 0600 file outside the project; none is in the files written beside it.
///
/// <para>Enforces cli/0006 (<c>docs/adr/0006-a-podman-environment-is-written-as-quadlet-units-not-run.md</c>).</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-podman-environment-is-written-as-quadlet-units-not-run.md")]
public partial class DeployPodmanTests
{
    private const string Adr =
        "see cli/0006 (docs/adr/0006-a-podman-environment-is-written-as-quadlet-units-not-run.md)";

    private const string Tag = "20261123-120000-abcdef123456";
    private const string Stack = "orders-home";
    private const string Dir = "/src/.trax/deploy/podman/orders-home";
    private const string SecretPath = "/home/u/.config/trax/orders-home/secret.yaml";

    private string _dir = "";
    private string _config = "";
    private Func<string> _configHome = () => "";

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-podman-{Guid.NewGuid():N}");
        _config = Path.Combine(_dir, "config");
        foreach (var service in File_().Services.Values)
        {
            var project = Path.Combine(_dir, service.Project);
            Directory.CreateDirectory(Path.GetDirectoryName(project) ?? _dir);
            await File.WriteAllTextAsync(project, "<Project />").ConfigureAwait(false);
        }
        _configHome = DeployKubernetesCommands.ConfigHome;
        DeployKubernetesCommands.ConfigHome = () => _config;
        DeployKubernetesCommands.Images = new KubernetesDeployerTests.FakeImages();
        DeployKubernetesCommands.Client = () =>
            throw new InvalidOperationException("a podman environment reached for a cluster");
    }

    [TearDown]
    public void TearDown()
    {
        DeployKubernetesCommands.ConfigHome = _configHome;
        DeployKubernetesCommands.Images = new DotnetContainerPublisher();
        DeployKubernetesCommands.Client = () =>
            new KubernetesDeployClient(DeployKubernetesCommands.Clock);
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    internal static DeployFile File_()
    {
        var file = DeployKubernetesTests.File_();
        file.Services["hub"].Port = 5080;
        file.Environments.Clear();
        file.Environments["home"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Kubernetes,
            Runtime = DeployEnvironment.Podman,
            Profile = DeployEnvironment.Try,
            Database = new DeployEnvironmentDatabase { InCluster = true },
            Settings = { ["Auth__Authority"] = "https://issuer.example.com" },
        };
        return file;
    }

    private static PodmanStack Render(DeployFile? file = null, bool rabbit = false) =>
        PodmanTarget.Render(
            file ?? File_(),
            "home",
            DeployKubernetesTests.Exports(rabbit),
            Tag,
            Dir,
            SecretPath
        );

    private static Dictionary<string, string> Files(PodmanStack stack) =>
        stack.Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    [Test]
    public void Every_pod_file_holds_only_kinds_podman_plays_and_nothing_only_a_cluster_reads()
    {
        var yaml = Render(rabbit: true)
            .Files.Where(f => f.Path.EndsWith(".yaml", StringComparison.Ordinal));

        foreach (var (path, content) in yaml)
        {
            Kinds
                .Matches(content)
                .Select(m => m.Groups["kind"].Value)
                .Should()
                .NotBeEmpty()
                .And.OnlyContain(k => PodmanTarget.PlayableKinds.Contains(k), $"{path}; {Adr}");
            content.Should().NotContain("hostUsers", path).And.NotContain("namespace:", path);
        }
    }

    [Test]
    public void Each_pod_is_named_after_the_stack_and_runs_a_full_image_name()
    {
        var files = Files(Render(rabbit: true));

        files
            .Keys.Should()
            .BeEquivalentTo([
                "orders-home.network",
                "orders-home-migrate.yaml",
                "orders-home-database.yaml",
                "orders-home-database.kube",
                "orders-home-rabbitmq.yaml",
                "orders-home-rabbitmq.kube",
                "orders-home-hub.yaml",
                "orders-home-hub.kube",
                "orders-home-reports.yaml",
                "orders-home-reports.kube",
                "orders-home-worker.yaml",
                "orders-home-worker.kube",
            ]);
        files["orders-home-hub.yaml"]
            .Should()
            .Contain("name: orders-home-hub")
            .And.Contain($"image: localhost/orders-hub:{Tag}");
        files["orders-home-migrate.yaml"]
            .Should()
            .Contain("name: orders-home-migrate")
            .And.Contain("--trax-migrate");
        files["orders-home-database.yaml"].Should().Contain("image: docker.io/library/postgres:17");
        files["orders-home-rabbitmq.yaml"].Should().Contain("image: docker.io/library/rabbitmq:4");
    }

    [Test]
    public void Every_document_separator_stands_on_a_line_of_its_own()
    {
        var stack = Render(rabbit: true);
        var secret = PodmanTarget.SecretYaml(
            stack,
            stack.SecretKeys.ToDictionary(k => k, _ => "v", StringComparer.Ordinal),
            "/src/.env"
        );

        foreach (
            var (path, content) in stack
                .Files.Where(f => f.Path.EndsWith(".yaml", StringComparison.Ordinal))
                .Append(("secret.yaml", secret))
        )
        {
            content
                .Split('\n')
                .Where(l => l.Contains("---", StringComparison.Ordinal))
                .Should()
                .OnlyContain(l => l == "---", path);
            content.Should().EndWith("\n", path);
        }
    }

    [Test]
    public void Postgres_is_a_Deployment_of_one_on_a_claim_podman_keeps_as_a_named_volume()
    {
        var database = Files(Render())["orders-home-database.yaml"];

        database
            .Should()
            .Contain("kind: PersistentVolumeClaim")
            .And.Contain("name: orders-home-data")
            .And.Contain("kind: Deployment")
            .And.Contain("claimName: orders-home-data")
            .And.NotContain("StatefulSet")
            .And.NotContain("volumeClaimTemplates");
    }

    [Test]
    public void The_migrator_waits_for_the_database_and_runs_the_migration_before_its_pod()
    {
        var hub = Files(Render())["orders-home-hub.kube"];

        hub.Should()
            .Contain("Requires=orders-home-database.service\nAfter=orders-home-database.service\n")
            .And.Contain($"Yaml={Dir}/orders-home-hub.yaml")
            .And.Contain("PublishPort=127.0.0.1:5080:8080");
        ExecStartPre(hub)
            .Should()
            .Equal(
                $"/usr/bin/podman kube play --replace {SecretPath}",
                "/bin/sh -c 'for i in $$(seq 120); do /usr/bin/podman exec orders-home-database-pod-database "
                    + "pg_isready -q -U orders && exit 0; sleep 1; done; echo \"the database did not become "
                    + "ready\" >&2; exit 1'",
                $"/usr/bin/podman kube play --replace --userns=nomap --network orders-home {Dir}/orders-home-migrate.yaml",
                "/bin/sh -c 'code=$$(/usr/bin/podman wait orders-home-migrate-pod-migrate); [ \"$$code\" = 0 ] || "
                    + "{ /usr/bin/podman logs --tail 50 orders-home-migrate-pod-migrate >&2; echo \"the migration "
                    + "failed (exit $$code), so no service was started\" >&2; exit 1; }'"
            );
    }

    [Test]
    public void Every_other_service_starts_after_the_migrator_and_runs_no_migration()
    {
        var worker = Files(Render())["orders-home-worker.kube"];

        worker
            .Should()
            .Contain("Requires=orders-home-hub.service\nAfter=orders-home-hub.service\n")
            .And.NotContain("PublishPort")
            .And.NotContain("migrate");
        ExecStartPre(worker).Should().Equal($"/usr/bin/podman kube play --replace {SecretPath}");
    }

    [Test]
    public void Every_unit_runs_on_the_apps_network_without_the_users_uid()
    {
        var files = Files(Render(rabbit: true));

        files["orders-home.network"].Should().Contain("[Network]\nNetworkName=orders-home\n");
        foreach (
            var (name, unit) in files.Where(f => f.Key.EndsWith(".kube", StringComparison.Ordinal))
        )
            unit.Should()
                .Contain("Network=orders-home.network\n", name)
                .And.Contain("UserNS=nomap\n", name)
                .And.Contain("ExitCodePropagation=any\n", name)
                .And.Contain("Restart=on-failure\n", name)
                .And.Contain("WantedBy=default.target\n", name);
    }

    [Test]
    public void No_unit_passes_a_shell_dollar_systemd_would_expand()
    {
        foreach (
            var (name, unit) in Render()
                .Files.Where(f => f.Path.EndsWith(".kube", StringComparison.Ordinal))
        )
            SingleDollar.IsMatch(unit).Should().BeFalse(name);
    }

    [Test]
    public void The_secrets_are_filled_from_env_and_no_value_is_in_the_files_beside_the_project()
    {
        var stack = Render();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["POSTGRES_PASSWORD"] = "pg-sentinel-1",
            ["Stripe__ApiKey"] = "sk-sentinel-2",
        };

        var secret = PodmanTarget.SecretYaml(stack, values, "/src/.env");

        secret
            .Should()
            .Contain("kind: Secret")
            .And.Contain("name: orders-home")
            .And.Contain("pg-sentinel-1")
            .And.Contain("sk-sentinel-2");
        stack
            .Files.Should()
            .NotContain(f => f.Content.Contains("sentinel", StringComparison.Ordinal));
        stack.SecretKeys.Should().BeEquivalentTo(["POSTGRES_PASSWORD", "Stripe__ApiKey"]);
    }

    [Test]
    public void A_key_env_lacks_is_refused_naming_it()
    {
        var act = () =>
            PodmanTarget.SecretYaml(
                Render(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["POSTGRES_PASSWORD"] = "x",
                },
                "/src/.env"
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("/src/.env has no value for Stripe__ApiKey*nothing was built or written*");
    }

    [Test]
    public void An_existing_database_is_read_from_a_secret_of_its_own_and_no_Postgres_runs()
    {
        var file = File_();
        file.Environments["home"].Profile = DeployEnvironment.Production;
        file.Environments["home"].Database = new DeployEnvironmentDatabase
        {
            Secret = "orders-db",
            Key = "OrdersDb",
        };

        var stack = Render(file);

        stack.Files.Should().NotContain(f => f.Path.Contains("database", StringComparison.Ordinal));
        stack.Secrets.Select(s => s.Metadata.Name).Should().Equal("orders-home", "orders-db");
        stack.SecretKeys.Should().Contain("OrdersDb");
        ExecStartPre(Files(stack)["orders-home-hub.kube"])
            .Should()
            .HaveCount(3, "no database to wait for");
    }

    [TestCase("namespace", "*namespace is for a cluster*")]
    [TestCase("registry", "*registry is for a cluster*")]
    [TestCase("runtime", "*runtime 'lxc' is not cluster or podman*")]
    public void A_podman_environment_refuses_what_only_a_cluster_reads(string what, string message)
    {
        var file = File_();
        var env = file.Environments["home"];
        switch (what)
        {
            case "namespace":
                env.Namespace = "orders";
                break;
            case "registry":
                env.Registry = "ghcr.io/acme";
                break;
            case "runtime":
                env.Runtime = "lxc";
                break;
        }

        FluentActions
            .Invoking(() => file.Environment("home"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(message);
    }

    [TestCase(DeployEnvironment.Aws)]
    [TestCase(DeployEnvironment.Compose)]
    public void Runtime_is_refused_on_any_other_target(string target)
    {
        var file = File_();
        file.Settings.Clear();
        file.Environments["home"] = new DeployEnvironment
        {
            Target = target,
            Runtime = DeployEnvironment.Podman,
            Region = string.Equals(target, DeployEnvironment.Aws, StringComparison.Ordinal)
                ? "us-west-2"
                : null,
            Profile = DeployEnvironment.Try,
        };

        FluentActions
            .Invoking(() => file.Environment("home"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*runtime is not a field a {target} environment reads*");
    }

    [Test]
    public void The_image_is_built_into_podman_when_the_environment_runs_there()
    {
        var psi = DotnetContainerPublisher.StartInfo(
            "/src/Orders/Orders.csproj",
            registry: null,
            "orders-hub",
            Tag,
            "podman"
        );

        psi.ArgumentList.Should()
            .Contain(
                "-p:LocalRegistry=Podman",
                "the SDK refuses any other spelling (CONTAINER2002)"
            )
            .And.Contain("-p:ContainerRepository=orders-hub");
    }

    [TestCase("podman", "Podman")]
    [TestCase("docker", "Docker")]
    public void The_local_runtime_is_named_as_the_sdk_spells_it(string runtime, string sdk) =>
        DotnetContainerPublisher.LocalRegistry(runtime).Should().Be(sdk);

    [Test]
    public void A_local_runtime_the_sdk_does_not_load_into_is_refused()
    {
        var act = () => DotnetContainerPublisher.LocalRegistry("containerd");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'containerd'*Use podman or docker*");
    }

    [Test]
    public async Task Deploy_builds_into_podman_and_writes_the_units_and_a_private_secret_file()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(_dir, ".env"),
                "# values\nPOSTGRES_PASSWORD='pg;pw'\nexport Stripe__ApiKey=\"sk_test\"\n"
            )
            .ConfigureAwait(false);
        using var stdout = new StringWriter();

        var exit = await DeployAsync(path, stdout).ConfigureAwait(false);

        exit.Should().Be(0, stdout.ToString());
        var images = (KubernetesDeployerTests.FakeImages)DeployKubernetesCommands.Images;
        images.Published.Should().Equal("orders-hub", "orders-reports", "orders-worker");
        images.LocalRuntimes.Should().OnlyContain(r => r == "podman");
        var deployed = Path.Combine(_dir, ".trax", "deploy", "podman", Stack);
        File.Exists(Path.Combine(deployed, "orders-home-hub.kube")).Should().BeTrue();
        var secret = Path.Combine(_config, "trax", Stack, "secret.yaml");
        (await File.ReadAllTextAsync(secret).ConfigureAwait(false))
            .Should()
            .Contain("pg;pw")
            .And.Contain("sk_test");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(secret)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        foreach (var written in Directory.EnumerateFiles(deployed))
            (await File.ReadAllTextAsync(written).ConfigureAwait(false))
                .Should()
                .NotContain("pg;pw", written);
        stdout
            .ToString()
            .Should()
            .Contain(
                "systemctl --user restart orders-home-database.service orders-home-hub.service"
            );
    }

    [Test]
    public async Task Deploy_without_a_secret_in_env_builds_and_writes_nothing()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_dir, ".env"), "POSTGRES_PASSWORD=x\n")
            .ConfigureAwait(false);
        using var stderr = new StringWriter();

        var exit = await DeployAsync(path, TextWriter.Null, stderr).ConfigureAwait(false);

        exit.Should().Be(1);
        stderr.ToString().Should().Contain("has no value for Stripe__ApiKey");
        ((KubernetesDeployerTests.FakeImages)DeployKubernetesCommands.Images)
            .Published.Should()
            .BeEmpty();
        Directory.Exists(Path.Combine(_dir, ".trax")).Should().BeFalse();
        Directory.Exists(_config).Should().BeFalse();
    }

    [Test]
    public async Task Status_on_a_podman_environment_points_at_systemd()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        using var stderr = new StringWriter();

        var exit = await DeployAwsCommands
            .StatusAsync(path, "home", TextWriter.Null, stderr, CancellationToken.None)
            .ConfigureAwait(false);

        exit.Should().Be(1);
        stderr
            .ToString()
            .Should()
            .Contain("runs under podman")
            .And.Contain("systemctl --user status orders-home-*");
    }

    [Test]
    public async Task A_dotenv_file_reads_as_compose_reads_it()
    {
        var path = Path.Combine(_dir, "x.env");
        await File.WriteAllTextAsync(
                path,
                "# c\n\nA=1\nexport B = two words \nC='q;\"x'\nD=\"y\"\nnot a line\nE=\n"
            )
            .ConfigureAwait(false);

        var values = await DotEnv.ReadAsync(path).ConfigureAwait(false);

        values
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["A"] = "1",
                    ["B"] = "two words",
                    ["C"] = "q;\"x",
                    ["D"] = "y",
                    ["E"] = "",
                }
            );
    }

    private Task<int> DeployAsync(string path, TextWriter stdout, TextWriter? stderr = null) =>
        DeployCommand.DispatchAsync(
            path,
            target: null,
            "home",
            outDir: null,
            build: false,
            new DeployKubernetesTests.Exporter(DeployKubernetesTests.Exports(), _dir),
            stdout,
            stderr ?? TextWriter.Null,
            CancellationToken.None
        );

    private async Task<string> WriteAsync(DeployFile file)
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
        return path;
    }

    private static List<string> ExecStartPre(string unit) =>
        [
            .. unit.Split('\n')
                .Where(l => l.StartsWith("ExecStartPre=", StringComparison.Ordinal))
                .Select(l => l["ExecStartPre=".Length..]),
        ];

    [GeneratedRegex(
        @"^kind: (?<kind>\w+)$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Kinds { get; }

    // A '$' systemd would expand: one not doubled.
    [GeneratedRegex(@"(?<!\$)\$(?!\$)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SingleDollar { get; }
}
