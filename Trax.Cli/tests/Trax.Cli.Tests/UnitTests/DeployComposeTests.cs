using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy --target compose</c> turns trax.deploy.json and what each host reports into a compose stack:
/// Postgres, RabbitMQ only when a host connects to it, one migrate run before any service starts, each service with
/// migrations off, every password, setting and secret required from the environment, and a port only for a hub (a
/// service that serves HTTP and runs the api or scheduler role, so runs as one). It refuses what compose cannot give
/// the hosts, and never overwrites a compose file it did not write.
/// </summary>
public partial class DeployComposeTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        foreach (
            var project in new[] { "Orders/Orders.csproj", "Orders.Worker/Orders.Worker.csproj" }
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, project)).NotNull());
            File.WriteAllText(Path.Combine(_dir, project), "<Project />");
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static DeployFile File_() =>
        new()
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
            Settings = ["Auth__Authority"],
            Secrets = ["Stripe__ApiKey"],
        };

    private static DeployManifest Hub(string provider = "postgres") =>
        new()
        {
            Roles = ["api", "scheduler"],
            Database = new DeployDatabase { Provider = provider, MigratesOnStart = true },
            Transports = ["rabbitmq"],
            Endpoints = new DeployEndpoints { GraphQL = "/trax/graphql", Health = "/trax/health" },
        };

    private static DeployManifest Worker() =>
        new()
        {
            Roles = ["worker"],
            Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
            Transports = ["rabbitmq"],
        };

    private static Dictionary<string, DeployManifest> Exports() =>
        new(StringComparer.Ordinal) { ["hub"] = Hub(), ["worker"] = Worker() };

    private const string HubAndTwoWorkersCompose = """
        # Written by `trax deploy --target compose` from trax.deploy.json. Running it again overwrites this file.
        # For rootless podman: every service runs with userns_mode nomap, which docker compose does not accept.
        # Passwords, settings and secrets come from the environment, or from a .env file beside this one: copy
        # .env.example to .env and fill it in. Never commit the .env.

        # What every Trax service is given.
        x-trax: &trax
          ConnectionStrings__Trax: Host=database;Database=orders;Username=orders;GSS Encryption Mode=Disable
          PGPASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}
          RabbitMq__Host: rabbitmq
          RabbitMq__UserName: orders
          RabbitMq__Password: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
          Auth__Authority: ${Auth__Authority:?set Auth__Authority}
          Stripe__ApiKey: ${Stripe__ApiKey:?set Stripe__ApiKey}

        services:
          database:
            image: postgres:17
            environment:
              POSTGRES_DB: orders
              POSTGRES_USER: orders
              POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}
            volumes:
              - database:/var/lib/postgresql/data
            healthcheck:
              test: ["CMD", "pg_isready", "-U", "orders"]
              interval: 5s
            cap_drop:
              - ALL
            cap_add:
              - CHOWN
              - DAC_OVERRIDE
              - FOWNER
              - SETGID
              - SETUID
            security_opt:
              - no-new-privileges:true
            userns_mode: nomap
            pids_limit: 512
            mem_limit: 1024m
            memswap_limit: 1024m
            restart: unless-stopped

          rabbitmq:
            image: rabbitmq:4
            environment:
              RABBITMQ_DEFAULT_USER: orders
              RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
            healthcheck:
              test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
              interval: 5s
            cap_drop:
              - ALL
            cap_add:
              - CHOWN
              - DAC_OVERRIDE
              - SETGID
              - SETUID
            security_opt:
              - no-new-privileges:true
            userns_mode: nomap
            pids_limit: 512
            mem_limit: 512m
            memswap_limit: 512m
            restart: unless-stopped

          # The new version migrates the database once, before any service of it starts.
          migrate:
            image: orders-hub:${TRAX_IMAGE_TAG:-latest}
            command: ["--trax-migrate"]
            environment: *trax
            cap_drop:
              - ALL
            security_opt:
              - no-new-privileges:true
            userns_mode: nomap
            pids_limit: 512
            mem_limit: 1024m
            memswap_limit: 1024m
            depends_on:
              database:
                condition: service_healthy
            restart: "no"

          hub:
            image: orders-hub:${TRAX_IMAGE_TAG:-latest}
            environment:
              <<: *trax
              TRAX_MIGRATIONS: skip
            ports:
              - "127.0.0.1:8080:8080"
            cap_drop:
              - ALL
            security_opt:
              - no-new-privileges:true
            userns_mode: nomap
            pids_limit: 512
            mem_limit: 1024m
            memswap_limit: 1024m
            depends_on:
              migrate:
                condition: service_completed_successfully
              rabbitmq:
                condition: service_healthy
            restart: unless-stopped

          worker:
            image: orders-worker:${TRAX_IMAGE_TAG:-latest}
            environment:
              <<: *trax
              TRAX_MIGRATIONS: skip
            cap_drop:
              - ALL
            security_opt:
              - no-new-privileges:true
            userns_mode: nomap
            pids_limit: 512
            mem_limit: 1024m
            memswap_limit: 1024m
            deploy:
              replicas: 2
            depends_on:
              migrate:
                condition: service_completed_successfully
              rabbitmq:
                condition: service_healthy
            restart: unless-stopped

        volumes:
          database:

        """;

    [Test]
    public void A_hub_and_two_workers_become_this_stack()
    {
        var files = ComposeTarget.Render(File_(), Exports());

        files.Select(f => f.Path).Should().Equal("compose.yaml", ".env.example");
        files[0].Content.ReplaceLineEndings("\n").Should().Be(HubAndTwoWorkersCompose);
        files[1]
            .Content.ReplaceLineEndings("\n")
            .Should()
            .Be(
                """
                # Copy to .env beside compose.yaml and fill in every value. Never commit the .env.
                POSTGRES_PASSWORD=
                RABBITMQ_PASSWORD=
                Auth__Authority=
                Stripe__ApiKey=
                # The tag of the images to run, as trax deploy --build tagged them.
                TRAX_IMAGE_TAG=latest

                """
            );
    }

    [Test]
    public void A_password_with_url_characters_still_connects()
    {
        const string password = "a/b;c";
        var compose = ComposeTarget.Render(File_(), Exports())[0].Content;

        // What compose hands every Trax service once it has read the .env.
        var env = Interpolated(
            compose,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["POSTGRES_PASSWORD"] = password,
                ["RABBITMQ_PASSWORD"] = password,
                ["Auth__Authority"] = "https://issuer.example.com",
                ["Stripe__ApiKey"] = "sk",
            }
        );

        env["PGPASSWORD"].Should().Be(password);
        env["ConnectionStrings__Trax"]
            .Split(';')
            .Select(p => p.Split('=')[0])
            .Should()
            .Equal("Host", "Database", "Username", "GSS Encryption Mode");
        env.Should().NotContainKey("ConnectionStrings__RabbitMq");
        env["RabbitMq__Host"].Should().Be("rabbitmq");
        env["RabbitMq__UserName"].Should().Be("orders");
        env["RabbitMq__Password"].Should().Be(password);
        env.Where(e => e.Value.Contains(password, StringComparison.Ordinal))
            .Select(e => e.Key)
            .Should()
            .BeEquivalentTo(
                ["PGPASSWORD", "RabbitMq__Password"],
                "a password reaches a host only as a value of its own"
            );
    }

    /// <summary>The <c>x-trax</c> settings with each <c>${NAME:?...}</c> replaced as compose does.</summary>
    private static Dictionary<string, string> Interpolated(
        string compose,
        IReadOnlyDictionary<string, string> values
    )
    {
        var lines = compose.ReplaceLineEndings("\n").Split('\n');
        var start = Array.IndexOf(lines, "x-trax: &trax") + 1;
        return lines[start..]
            .TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal))
            .Select(l => l.Trim().Split(": ", 2))
            .ToDictionary(
                p => p[0],
                p => Required.Replace(p[1], m => values[m.Groups["name"].Value]),
                StringComparer.Ordinal
            );
    }

    [GeneratedRegex(
        @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*):\?[^}]*\}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Required { get; }

    [Test]
    public void A_stack_with_no_rabbitmq_host_runs_no_broker()
    {
        var hub = Hub();
        hub.Transports = [];
        var worker = Worker();
        worker.Transports = [];

        var compose = ComposeTarget
            .Render(
                File_(),
                new Dictionary<string, DeployManifest>(StringComparer.Ordinal)
                {
                    ["hub"] = hub,
                    ["worker"] = worker,
                }
            )[0]
            .Content;

        compose.Should().NotContain("rabbitmq").And.NotContain("RABBITMQ");
    }

    [TestCase("in-memory")]
    [TestCase("sqlite")]
    public void A_host_that_does_not_keep_its_runs_in_Postgres_is_refused(string provider)
    {
        var act = () =>
            ComposeTarget.Render(
                File_(),
                new Dictionary<string, DeployManifest>(StringComparer.Ordinal)
                {
                    ["hub"] = Hub(provider),
                    ["worker"] = Worker(),
                }
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'hub'*{provider}*UsePostgres*");
    }

    [Test]
    public void A_service_running_the_api_or_the_scheduler_runs_as_one()
    {
        var file = File_();
        file.Services["hub"].Count = 2;

        var act = () => ComposeTarget.Render(file, Exports());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'hub'*api and scheduler*count 2*");
    }

    [Test]
    public void A_replicated_service_that_reports_health_publishes_no_port()
    {
        var worker = Worker();
        worker.Endpoints = new DeployEndpoints { Health = "/trax/health" };

        var compose = ComposeTarget
            .Render(
                File_(),
                new Dictionary<string, DeployManifest>(StringComparer.Ordinal)
                {
                    ["hub"] = Hub(),
                    ["worker"] = worker,
                }
            )[0]
            .Content;

        compose.Split("127.0.0.1:").Length.Should().Be(2, "only the hub publishes a port");
        compose.Should().Contain("\"127.0.0.1:8080:8080\"");
    }

    [Test]
    public void A_hub_takes_the_next_port_no_other_service_has_asked_for()
    {
        var file = File_();
        file.Services["ops"] = new DeployService { Project = "Orders/Orders.csproj", Port = 8080 };
        var ops = Hub();
        ops.Roles = ["scheduler"];

        var compose = ComposeTarget
            .Render(
                file,
                new Dictionary<string, DeployManifest>(StringComparer.Ordinal)
                {
                    ["hub"] = Hub(),
                    ["worker"] = Worker(),
                    ["ops"] = ops,
                }
            )[0]
            .Content;

        compose.Should().Contain("\"127.0.0.1:8081:8080\"").And.Contain("\"127.0.0.1:8080:8080\"");
        compose.Split("127.0.0.1:8080:").Length.Should().Be(2);
    }

    [Test]
    public void A_port_on_a_service_compose_publishes_no_port_for_is_refused()
    {
        var file = File_();
        file.Services["worker"].Port = 9000;

        var act = () => ComposeTarget.Render(file, Exports());

        act.Should().Throw<InvalidOperationException>().WithMessage("*'worker'*port 9000*");
    }

    [TestCase(typeof(UnauthorizedAccessException))]
    [TestCase(typeof(System.Text.Json.JsonException))]
    [TestCase(typeof(System.ComponentModel.Win32Exception))]
    public async Task Deploy_reports_a_failure_in_a_line(Type failure)
    {
        var exit = await DeployCommand
            .RunAsync(
                "compose",
                await WriteAsync(File_()).ConfigureAwait(false),
                outDir: null,
                build: false,
                new AwsDeployerTests.ThrowingExporter(
                    (Exception)Activator.CreateInstance(failure).NotNull()
                ),
                (_, _) => Task.CompletedTask,
                TextWriter.Null,
                TextWriter.Null
            )
            .ConfigureAwait(false);

        exit.Should().Be(1);
    }

    [Test]
    public async Task A_child_that_does_not_exit_in_time_is_stopped_and_its_output_kept()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Uses sh; the timeout's code path is the same on every OS.");
        var psi = new System.Diagnostics.ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("echo serving; sleep 20");
        var clock = TimeProvider.System.GetTimestamp();

        var act = async () =>
            await ChildProcess
                .RunAsync(psi, TimeSpan.FromSeconds(1), CancellationToken.None)
                .ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage("*serving*")
            .ConfigureAwait(false);
        TimeProvider.System.GetElapsedTime(clock).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task A_child_is_stopped_when_the_command_is_cancelled()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Uses sh; the cancellation's code path is the same on every OS.");
        var psi = new System.Diagnostics.ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep 20");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var clock = TimeProvider.System.GetTimestamp();

        var act = async () =>
            await ChildProcess
                .RunAsync(psi, TimeSpan.FromMinutes(5), cancel.Token)
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        TimeProvider.System.GetElapsedTime(clock).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Deploy_writes_the_stack_beside_the_file_from_what_the_hosts_report()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        var exporter = new FakeExporter(Exports(), _dir);

        var exit = await DeployCommand
            .RunAsync(
                "compose",
                path,
                outDir: null,
                build: false,
                exporter,
                (_, _) => Task.CompletedTask,
                TextWriter.Null,
                TextWriter.Null
            )
            .ConfigureAwait(false);

        exit.Should().Be(0);
        File.Exists(Path.Combine(_dir, "compose.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_dir, ".env.example")).Should().BeTrue();
        exporter
            .Asked.Should()
            .Equal(
                Path.Combine(_dir, "Orders", "Orders.csproj"),
                Path.Combine(_dir, "Orders.Worker", "Orders.Worker.csproj")
            );
        exporter.Environment.NotNull()["ConnectionStrings__Trax"].Should().Contain(".invalid");
        exporter.Environment["Auth__Authority"].Should().StartWith("https://");
        exporter.Environment.Should().ContainKey("Stripe__ApiKey");
    }

    [Test]
    public async Task Deploy_does_not_overwrite_a_compose_file_it_did_not_write()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_dir, "compose.yaml"), "services: {}\n")
            .ConfigureAwait(false);

        var exit = await DeployCommand
            .RunAsync(
                "compose",
                path,
                outDir: null,
                build: false,
                new FakeExporter(Exports(), _dir),
                (_, _) => Task.CompletedTask,
                TextWriter.Null,
                TextWriter.Null
            )
            .ConfigureAwait(false);

        exit.Should().Be(1);
        (await File.ReadAllTextAsync(Path.Combine(_dir, "compose.yaml")).ConfigureAwait(false))
            .Should()
            .Be("services: {}\n");
    }

    [Test]
    public async Task Deploy_builds_each_image_under_its_service_name_when_asked()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);
        var built = new List<(string Project, string Image)>();

        (
            await DeployCommand
                .RunAsync(
                    "compose",
                    path,
                    outDir: null,
                    build: true,
                    new FakeExporter(Exports(), _dir),
                    (p, i) =>
                    {
                        built.Add((p, i));
                        return Task.CompletedTask;
                    },
                    TextWriter.Null,
                    TextWriter.Null
                )
                .ConfigureAwait(false)
        ).Should().Be(0);

        built.Select(b => b.Image).Should().Equal("orders-hub", "orders-worker");
    }

    [Test]
    public async Task A_target_trax_does_not_deploy_to_is_refused() =>
        (
            await DeployCommand
                .RunAsync(
                    "gcp",
                    await WriteAsync(File_()).ConfigureAwait(false),
                    outDir: null,
                    build: false,
                    new FakeExporter(Exports(), _dir),
                    (_, _) => Task.CompletedTask,
                    TextWriter.Null,
                    TextWriter.Null
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Be(1);

    [Test]
    public async Task The_file_reads_back_what_it_wrote()
    {
        var path = await WriteAsync(File_()).ConfigureAwait(false);

        var read = await DeployFile.ReadAsync(path).ConfigureAwait(false);

        read.Should().BeEquivalentTo(File_());
    }

    [Test]
    public void A_service_runs_in_the_memory_it_names()
    {
        var file = File_();
        file.Services["worker"].Memory = 2048;

        var compose = ComposeTarget.Render(file, Exports())[0].Content.ReplaceLineEndings("\n");

        Block(compose, "worker").Should().Contain("    mem_limit: 2048m\n");
        Block(compose, "hub").Should().Contain("    mem_limit: 1024m\n");
    }

    [Test]
    public void Every_service_drops_every_capability_and_adds_back_only_its_own()
    {
        var compose = ComposeTarget.Render(File_(), Exports())[0].Content.ReplaceLineEndings("\n");

        foreach (var service in new[] { "database", "rabbitmq", "migrate", "hub", "worker" })
        {
            var block = Block(compose, service);
            block.Should().Contain("    cap_drop:\n      - ALL\n", service);
            block.Should().Contain("    userns_mode: nomap\n", service);
            block.Should().Contain("    pids_limit: 512\n", service);
            block.Should().MatchRegex(@"    mem_limit: (\d+)m\n    memswap_limit: \1m\n", service);
        }
        Block(compose, "hub").Should().NotContain("cap_add");
        Block(compose, "worker").Should().NotContain("cap_add");
    }

    /// <summary>The lines of one service in a rendered compose file, up to the next service.</summary>
    private static string Block(string compose, string service)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"the stack has a {service} service");
        var end = compose.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        return compose[start..(end < 0 ? compose.Length : end)];
    }

    [TestCase(
        """{ "version": 1, "app": "Orders", "services": { "hub": { "project": "Orders/Orders.csproj" } } }""",
        "*app 'Orders'*"
    )]
    [TestCase("""{ "version": 1, "app": "orders", "services": {} }""", "*services is empty*")]
    [TestCase(
        """{ "version": 1, "app": "orders", "services": { "migrate": { "project": "Orders/Orders.csproj" } } }""",
        "*service 'migrate'*"
    )]
    [TestCase(
        """{ "version": 1, "app": "orders", "services": { "hub": { "project": "Nope/Nope.csproj" } } }""",
        "*'Nope/Nope.csproj'*does not exist*"
    )]
    [TestCase(
        """{ "version": 1, "app": "orders", "services": { "hub": { "project": "Orders/Orders.csproj" } }, "secrets": ["Auth:Authority"] }""",
        "*'Auth:Authority' is not an environment name*"
    )]
    [TestCase(
        """{ "version": 1, "app": "orders", "services": { "hub": { "project": "Orders/Orders.csproj", "port": 8080 }, "worker": { "project": "Orders.Worker/Orders.Worker.csproj", "port": 8080 } } }""",
        "*'worker'*port 8080*'hub'*"
    )]
    [TestCase(
        """{ "version": 1, "app": "orders", "services": { "hub": { "project": "Orders/Orders.csproj", "memory": 32 } } }""",
        "*service 'hub' has memory 32*"
    )]
    [TestCase("""{ "version": 2, "app": "orders", "services": {} }""", "*version 2*")]
    public async Task A_file_that_cannot_be_deployed_is_refused_naming_the_problem(
        string json,
        string message
    )
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, json);

        var act = async () => await DeployFile.ReadAsync(path).ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage(message);
    }

    [Test]
    public async Task Init_names_each_Trax_host_under_the_directory_and_skips_tests()
    {
        await WriteProjectAsync("Orders/Orders.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api")
            .ConfigureAwait(false);
        await WriteProjectAsync(
                "Orders.Worker/Orders.Worker.csproj",
                "Microsoft.NET.Sdk.Worker",
                "Trax.Scheduler",
                exe: true
            )
            .ConfigureAwait(false);
        await WriteProjectAsync(
                "Orders.Trains/Orders.Trains.csproj",
                "Microsoft.NET.Sdk",
                "Trax.Effect"
            )
            .ConfigureAwait(false);
        await WriteProjectAsync(
                "tests/Orders.Tests/Orders.Tests.csproj",
                "Microsoft.NET.Sdk.Web",
                "Trax.Api"
            )
            .ConfigureAwait(false);
        await WriteProjectAsync(
                "Unrelated/Unrelated.csproj",
                "Microsoft.NET.Sdk.Web",
                "Newtonsoft.Json"
            )
            .ConfigureAwait(false);

        var file = DeployInit.Create(_dir, "orders");

        file.Services.Keys.Should().Equal("orders", "worker");
        file.Services["orders"].Project.Should().Be("Orders/Orders.csproj");
        file.Services["worker"].Project.Should().Be("Orders.Worker/Orders.Worker.csproj");
    }

    [Test]
    public async Task Init_calls_a_lone_host_the_hub()
    {
        await WriteProjectAsync("App/App.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api")
            .ConfigureAwait(false);

        DeployInit.Create(_dir, "orders").Services.Keys.Should().Equal("hub");
    }

    [Test]
    public async Task Init_does_not_replace_an_existing_file_without_force()
    {
        await WriteProjectAsync("App/App.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api")
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_dir, DeployFile.FileName), "{}")
            .ConfigureAwait(false);

        (
            await DeployCommand
                .InitAsync(
                    _dir,
                    "orders",
                    force: false,
                    "compose",
                    region: null,
                    domain: null,
                    TextWriter.Null,
                    TextWriter.Null
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Be(1);
        (await File.ReadAllTextAsync(Path.Combine(_dir, DeployFile.FileName)).ConfigureAwait(false))
            .Should()
            .Be("{}");
        (
            await DeployCommand
                .InitAsync(
                    _dir,
                    "orders",
                    force: true,
                    "compose",
                    region: null,
                    domain: null,
                    TextWriter.Null,
                    TextWriter.Null
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Be(0);
    }

    [Test]
    public void The_export_runs_the_project_in_Production_with_the_argument()
    {
        var psi = DotnetHostExporter.StartInfo(
            "/src/Orders/Orders.csproj",
            "/tmp/m.json",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["X"] = "y" }
        );

        psi.ArgumentList.Should()
            .Equal(
                "run",
                "--project",
                "/src/Orders/Orders.csproj",
                "-c",
                "Release",
                "--no-launch-profile",
                "--",
                "--trax-export-deploy",
                "/tmp/m.json"
            );
        psi.Environment["ASPNETCORE_ENVIRONMENT"].Should().Be("Production");
        psi.Environment["X"].Should().Be("y");
    }

    private async Task<string> WriteAsync(DeployFile file)
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
        return path;
    }

    private async Task WriteProjectAsync(
        string relative,
        string sdk,
        string reference,
        bool exe = false
    )
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path).NotNull());
        await File.WriteAllTextAsync(
                path,
                $"""
                <Project Sdk="{sdk}">
                  <PropertyGroup>{(exe ? "<OutputType>Exe</OutputType>" : "")}</PropertyGroup>
                  <ItemGroup><PackageReference Include="{reference}" /></ItemGroup>
                </Project>
                """
            )
            .ConfigureAwait(false);
    }

    private sealed class FakeExporter(
        IReadOnlyDictionary<string, DeployManifest> byService,
        string dir
    ) : IHostExporter
    {
        public List<string> Asked { get; } = [];

        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public Task<DeployManifest> ExportAsync(
            string projectPath,
            IReadOnlyDictionary<string, string> environment
        )
        {
            Asked.Add(projectPath);
            Environment = environment;
            return Task.FromResult(
                Path.GetRelativePath(dir, projectPath)
                    .StartsWith("Orders.Worker", StringComparison.Ordinal)
                    ? byService["worker"]
                    : byService["hub"]
            );
        }
    }
}
