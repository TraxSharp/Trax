using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Core.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A process started with <c>--trax-migrate</c> brings the contexts named with <c>MigrateOnDeploy</c> up to date and
/// returns without starting the host.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MigrateOnDeployTests
{
    private const string Schema = "migrate_on_deploy_tests";

    private string? _connectionString;

    [SetUp]
    public async Task DropTheSchema()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        _connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"].NotNull()
        );
        await Exec($"DROP SCHEMA IF EXISTS {Schema} CASCADE;");
    }

    [TearDown]
    public async Task DropTheSchemaAfter() =>
        await Exec($"DROP SCHEMA IF EXISTS {Schema} CASCADE;");

    [Test]
    public async Task A_migrate_process_creates_the_named_contexts_tables_and_does_not_start_the_host()
    {
        var services = Services();
        await using var servicesDisposal = services.ConfigureAwait(false);
        var host = new UnstartedHost(services);

        await host.RunTraxAsync(["--trax-migrate"]);

        host.Started.Should().BeFalse();
        (await TableExists("gadgets")).Should().BeTrue();
    }

    [Test]
    public async Task A_second_migrate_run_changes_nothing()
    {
        var services = Services();
        await using var servicesDisposal = services.ConfigureAwait(false);
        var host = new UnstartedHost(services);
        await host.RunTraxAsync(["--trax-migrate"]);
        await Exec($"INSERT INTO {Schema}.gadgets (name) VALUES ('kept');");

        await host.RunTraxAsync(["--trax-migrate"]);

        (await Count("gadgets")).Should().Be(1);
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<GadgetContext>(o => o.UseNpgsql(_connectionString.NotNull()));
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UsePostgres(_connectionString.NotNull()).MigrateOnDeploy<GadgetContext>()
            )
        );
        return services.BuildServiceProvider();
    }

    private async Task Exec(string sql)
    {
        var connection = new NpgsqlConnection(_connectionString.NotNull());
        await using var connectionDisposal = connection.ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        var command = new NpgsqlCommand(sql, connection);
        await using var commandDisposal = command.ConfigureAwait(false);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<bool> TableExists(string table)
    {
        var connection = new NpgsqlConnection(_connectionString.NotNull());
        await using var connectionDisposal = connection.ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @s AND table_name = @t)",
            connection
        );
        await using var commandDisposal = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("s", Schema);
        command.Parameters.AddWithValue("t", table);
        return (bool)(await command.ExecuteScalarAsync().ConfigureAwait(false)).NotNull();
    }

    private async Task<long> Count(string table)
    {
        var connection = new NpgsqlConnection(_connectionString.NotNull());
        await using var connectionDisposal = connection.ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        var command = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.{table}", connection);
        await using var commandDisposal = command.ConfigureAwait(false);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false)).NotNull();
    }

    /// <summary>A host that records whether anything started it.</summary>
    private sealed class UnstartedHost(IServiceProvider services) : IHost
    {
        public bool Started { get; private set; }

        public IServiceProvider Services { get; } = services;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }

    public sealed class Gadget
    {
        public int Id { get; set; }
        public required string Name { get; set; }
    }

    public sealed class GadgetContext(DbContextOptions<GadgetContext> options)
        : DomainDataContext<GadgetContext>(options)
    {
        protected override string Schema => MigrateOnDeployTests.Schema;

        protected override void ConfigureModel(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Gadget>(e =>
            {
                e.ToTable("gadgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
    }
}
