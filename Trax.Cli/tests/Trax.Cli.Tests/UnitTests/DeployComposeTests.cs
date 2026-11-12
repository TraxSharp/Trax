using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax deploy --target compose</c> turns trax.deploy.json and what each host reports into a compose stack:
/// Postgres, RabbitMQ only when a host connects to it, one migrate run before any service starts, each service with
/// migrations off, every password, setting and secret required from the environment, and a port only for a service
/// that serves HTTP. It refuses what compose cannot give the hosts, and never overwrites a compose file it did not
/// write.
/// </summary>
public class DeployComposeTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        foreach (
            var project in new[] { "Orders/Orders.csproj", "Orders.Worker/Orders.Worker.csproj" }
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, project))!);
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
        new() { ["hub"] = Hub(), ["worker"] = Worker() };

    [Test]
    public void A_hub_and_two_workers_become_this_stack()
    {
        var files = ComposeTarget.Render(File_(), Exports());

        files.Select(f => f.Path).Should().Equal("compose.yaml", ".env.example");
        files[0]
            .Content.ReplaceLineEndings("\n")
            .Should()
            .Be(
                """
                # Written by `trax deploy --target compose` from trax.deploy.json. Running it again overwrites this file.
                # Passwords, settings and secrets come from the environment, or from a .env file beside this one: copy
                # .env.example to .env and fill it in. Never commit the .env.

                # What every Trax service is given.
                x-trax: &trax
                  ConnectionStrings__Trax: Host=database;Database=orders;Username=orders;Password=${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD};GSS Encryption Mode=Disable
                  ConnectionStrings__RabbitMq: amqp://orders:${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}@rabbitmq:5672
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
                    security_opt:
                      - no-new-privileges:true
                    restart: unless-stopped

                  rabbitmq:
                    image: rabbitmq:4
                    environment:
                      RABBITMQ_DEFAULT_USER: orders
                      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
                    healthcheck:
                      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
                      interval: 5s
                    security_opt:
                      - no-new-privileges:true
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

                """
            );
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
    public void A_stack_with_no_rabbitmq_host_runs_no_broker()
    {
        var hub = Hub();
        hub.Transports = [];
        var worker = Worker();
        worker.Transports = [];

        var compose = ComposeTarget
            .Render(
                File_(),
                new Dictionary<string, DeployManifest> { ["hub"] = hub, ["worker"] = worker }
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
                new Dictionary<string, DeployManifest>
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
    public void Deploy_writes_the_stack_beside_the_file_from_what_the_hosts_report()
    {
        var path = Write(File_());
        var exporter = new FakeExporter(Exports(), _dir);

        var exit = DeployCommand.Run("compose", path, null, build: false, exporter, (_, _) => { });

        exit.Should().Be(0);
        File.Exists(Path.Combine(_dir, "compose.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_dir, ".env.example")).Should().BeTrue();
        exporter
            .Asked.Should()
            .Equal(
                Path.Combine(_dir, "Orders", "Orders.csproj"),
                Path.Combine(_dir, "Orders.Worker", "Orders.Worker.csproj")
            );
        exporter.Environment!["ConnectionStrings__Trax"].Should().Contain(".invalid");
        exporter.Environment["Auth__Authority"].Should().StartWith("https://");
        exporter.Environment.Should().ContainKey("Stripe__ApiKey");
    }

    [Test]
    public void Deploy_does_not_overwrite_a_compose_file_it_did_not_write()
    {
        var path = Write(File_());
        File.WriteAllText(Path.Combine(_dir, "compose.yaml"), "services: {}\n");

        var exit = DeployCommand.Run(
            "compose",
            path,
            null,
            build: false,
            new FakeExporter(Exports(), _dir),
            (_, _) => { }
        );

        exit.Should().Be(1);
        File.ReadAllText(Path.Combine(_dir, "compose.yaml")).Should().Be("services: {}\n");
    }

    [Test]
    public void Deploy_builds_each_image_under_its_service_name_when_asked()
    {
        var path = Write(File_());
        var built = new List<(string Project, string Image)>();

        DeployCommand
            .Run(
                "compose",
                path,
                null,
                build: true,
                new FakeExporter(Exports(), _dir),
                (p, i) => built.Add((p, i))
            )
            .Should()
            .Be(0);

        built.Select(b => b.Image).Should().Equal("orders-hub", "orders-worker");
    }

    [Test]
    public void A_target_trax_does_not_deploy_to_is_refused() =>
        DeployCommand
            .Run(
                "gcp",
                Write(File_()),
                null,
                false,
                new FakeExporter(Exports(), _dir),
                (_, _) => { }
            )
            .Should()
            .Be(1);

    [Test]
    public void The_file_reads_back_what_it_wrote()
    {
        var path = Write(File_());

        var read = DeployFile.Read(path);

        read.Should().BeEquivalentTo(File_());
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
    [TestCase("""{ "version": 2, "app": "orders", "services": {} }""", "*version 2*")]
    public void A_file_that_cannot_be_deployed_is_refused_naming_the_problem(
        string json,
        string message
    )
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        File.WriteAllText(path, json);

        var act = () => DeployFile.Read(path);

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public void Init_names_each_Trax_host_under_the_directory_and_skips_tests()
    {
        WriteProject("Orders/Orders.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api");
        WriteProject(
            "Orders.Worker/Orders.Worker.csproj",
            "Microsoft.NET.Sdk.Worker",
            "Trax.Scheduler",
            exe: true
        );
        WriteProject("Orders.Trains/Orders.Trains.csproj", "Microsoft.NET.Sdk", "Trax.Effect");
        WriteProject("tests/Orders.Tests/Orders.Tests.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api");
        WriteProject("Unrelated/Unrelated.csproj", "Microsoft.NET.Sdk.Web", "Newtonsoft.Json");

        var file = DeployInit.Create(_dir, "orders");

        file.Services.Keys.Should().Equal("orders", "worker");
        file.Services["orders"].Project.Should().Be("Orders/Orders.csproj");
        file.Services["worker"].Project.Should().Be("Orders.Worker/Orders.Worker.csproj");
    }

    [Test]
    public void Init_calls_a_lone_host_the_hub()
    {
        WriteProject("App/App.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api");

        DeployInit.Create(_dir, "orders").Services.Keys.Should().Equal("hub");
    }

    [Test]
    public void Init_does_not_replace_an_existing_file_without_force()
    {
        WriteProject("App/App.csproj", "Microsoft.NET.Sdk.Web", "Trax.Api");
        File.WriteAllText(Path.Combine(_dir, DeployFile.FileName), "{}");

        DeployCommand.Init(_dir, "orders", force: false).Should().Be(1);
        File.ReadAllText(Path.Combine(_dir, DeployFile.FileName)).Should().Be("{}");
        DeployCommand.Init(_dir, "orders", force: true).Should().Be(0);
    }

    [Test]
    public void The_export_runs_the_project_in_Production_with_the_argument()
    {
        var psi = DotnetHostExporter.StartInfo(
            "/src/Orders/Orders.csproj",
            "/tmp/m.json",
            new Dictionary<string, string> { ["X"] = "y" }
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

    private string Write(DeployFile file)
    {
        var path = Path.Combine(_dir, DeployFile.FileName);
        File.WriteAllText(path, file.Write());
        return path;
    }

    private void WriteProject(string relative, string sdk, string reference, bool exe = false)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>{(exe ? "<OutputType>Exe</OutputType>" : "")}</PropertyGroup>
              <ItemGroup><PackageReference Include="{reference}" /></ItemGroup>
            </Project>
            """
        );
    }

    private sealed class FakeExporter(
        IReadOnlyDictionary<string, DeployManifest> byService,
        string dir
    ) : IHostExporter
    {
        public List<string> Asked { get; } = [];

        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public DeployManifest Export(
            string projectPath,
            IReadOnlyDictionary<string, string> environment
        )
        {
            Asked.Add(projectPath);
            Environment = environment;
            return Path.GetRelativePath(dir, projectPath)
                .StartsWith("Orders.Worker", StringComparison.Ordinal)
                ? byService["worker"]
                : byService["hub"];
        }
    }
}
