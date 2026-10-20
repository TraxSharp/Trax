using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DeployMigration;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// The deploy export's argument, what a data provider records for it, the manifest's format, and the gate that keeps
/// an exporting process from serving.
/// </summary>
[TestFixture]
public class TraxDeployExportTests
{
    private const string UnreachableConnectionString =
        "Host=192.0.2.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    [TestCase(new[] { "--trax-export-deploy", "out/deploy.json" }, "out/deploy.json")]
    [TestCase(new[] { "--urls", "x", "--trax-export-deploy=deploy.json" }, "deploy.json")]
    [TestCase(new string[0], null)]
    public void The_path_follows_the_argument(string[] args, string? expected) =>
        TraxDeployExport.PathIn(args).Should().Be(expected);

    [TestCase("--trax-export-deploy")]
    [TestCase("--trax-export-deploy --urls")]
    [TestCase("--trax-export-deploy=")]
    public void The_argument_without_a_path_is_refused(string commandLine)
    {
        var act = () => TraxDeployExport.PathIn(commandLine.Split(' '));

        act.Should().Throw<ArgumentException>().WithMessage("*--trax-export-deploy*no path*");
    }

    [Test]
    public void An_exporting_process_skips_the_database_and_refuses_to_serve()
    {
        var services = new ServiceCollection();

        TraxDeployExport.SkipsDatabase(services, requested: true).Should().BeTrue();

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(DeployExportServeGate)
            );
    }

    [Test]
    public async Task The_gate_refuses_an_exporting_process_that_starts_to_serve()
    {
        var act = async () => await new DeployExportServeGate().StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*--trax-export-deploy*RunTraxAsync*");
    }

    [Test]
    public async Task Postgres_is_reported_with_whether_the_host_migrates_on_start()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.SkipMigrations().UsePostgres(UnreachableConnectionString)
            )
        );
        await using var provider = services.BuildServiceProvider();

        var manifest = await DeployManifestExport.BuildAsync(
            provider,
            host: null,
            CancellationToken.None
        );

        manifest.Database!.Provider.Should().Be("postgres");
        manifest.Database.MigratesOnStart.Should().BeFalse();
        manifest.Roles.Should().BeEmpty();
    }

    [Test]
    public async Task The_in_memory_provider_is_reported()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        await using var provider = services.BuildServiceProvider();

        var manifest = await DeployManifestExport.BuildAsync(
            provider,
            host: null,
            CancellationToken.None
        );

        manifest.Database!.Provider.Should().Be("in-memory");
    }

    [Test]
    public void A_manifest_reads_back_what_it_wrote()
    {
        var manifest = new DeployManifest
        {
            Roles = ["api", "scheduler"],
            Database = new DeployDatabase { Provider = "postgres", MigratesOnStart = true },
            Transports = ["rabbitmq"],
            Endpoints = new DeployEndpoints { GraphQL = "/trax/graphql", Health = "/trax/health" },
            Authentication =
            [
                new DeployAuthenticationScheme { Name = "Bearer", Handler = "JwtBearerHandler" },
            ],
        };

        var read = DeployManifest.Read(manifest.Write());

        read.Should().BeEquivalentTo(manifest);
    }

    [Test]
    public void A_manifest_from_a_newer_format_is_refused()
    {
        var act = () => DeployManifest.Read("""{ "version": 2 }""");

        act.Should().Throw<InvalidOperationException>().WithMessage("*format version 2*");
    }
}
