using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
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

    private string _connectionString = null!;

    [SetUp]
    public async Task DropTheSchema()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        _connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );
        await Exec($"DROP SCHEMA IF EXISTS {Schema} CASCADE;");
    }

    [TearDown]
    public async Task DropTheSchemaAfter() =>
        await Exec($"DROP SCHEMA IF EXISTS {Schema} CASCADE;");

    [Test]
    public async Task A_migrate_process_creates_the_named_contexts_tables_and_does_not_start_the_host()
    {
        await using var services = Services();
        var host = new UnstartedHost(services);

        await host.RunTraxAsync(["--trax-migrate"]);

        host.Started.Should().BeFalse();
        (await TableExists("gadgets")).Should().BeTrue();
    }

    [Test]
    public async Task A_second_migrate_run_changes_nothing()
    {
        await using var services = Services();
        var host = new UnstartedHost(services);
        await host.RunTraxAsync(["--trax-migrate"]);
        await Exec($"INSERT INTO {Schema}.gadgets (name) VALUES ('kept');");

        await host.RunTraxAsync(["--trax-migrate"]);

        (await Count("gadgets")).Should().Be(1);
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<GadgetContext>(o => o.UseNpgsql(_connectionString));
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UsePostgres(_connectionString).MigrateOnDeploy<GadgetContext>()
            )
        );
        return services.BuildServiceProvider();
    }

    private async Task Exec(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> TableExists(string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @s AND table_name = @t)",
            connection
        );
        command.Parameters.AddWithValue("s", Schema);
        command.Parameters.AddWithValue("t", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> Count(string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {Schema}.{table}",
            connection
        );
        return (long)(await command.ExecuteScalarAsync())!;
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
        public string Name { get; set; } = null!;
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
