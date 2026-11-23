using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectBuilder;
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

    [Test]
    public async Task A_migrate_process_applies_a_contexts_ef_migrations()
    {
        await using var services = Services(s =>
        {
            s.AddDbContextFactory<WidgetContext>(o =>
                o.UseNpgsql(
                        _connectionString,
                        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema)
                    )
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            );
            return effects => effects.MigrateOnDeploy<WidgetContext>();
        });
        var host = new UnstartedHost(services);

        await host.RunTraxAsync(["--trax-migrate"]);

        host.Started.Should().BeFalse();
        (await TableExists("widgets")).Should().BeTrue();
        (await AppliedMigrations()).Should().Equal(CreateWidgets.Id);
    }

    [Test]
    public async Task A_context_with_no_factory_refuses_the_migration()
    {
        await using var services = Services(_ =>
            effects => effects.MigrateOnDeploy<WidgetContext>()
        );
        var host = new UnstartedHost(services);

        var act = async () => await host.RunTraxAsync(["--trax-migrate"]).ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*MigrateOnDeploy<WidgetContext>()*no IDbContextFactory<WidgetContext>*");
        host.Started.Should().BeFalse();
    }

    private ServiceProvider Services() =>
        Services(s =>
        {
            s.AddDbContextFactory<GadgetContext>(o => o.UseNpgsql(_connectionString.NotNull()));
            return effects => effects.MigrateOnDeploy<GadgetContext>();
        });

    private ServiceProvider Services(
        Func<
            IServiceCollection,
            Func<TraxEffectBuilderWithData, TraxEffectBuilderWithData>
        > register
    )
    {
        var services = new ServiceCollection();
        var named = register(services);
        services.AddTrax(trax =>
            trax.AddEffects(effects => named(effects.UsePostgres(_connectionString.NotNull())))
        );
        return services.BuildServiceProvider();
    }

    private async Task<List<string>> AppliedMigrations()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(
                $"SELECT \"MigrationId\" FROM {Schema}.\"__EFMigrationsHistory\" ORDER BY 1",
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                var ids = new List<string>();
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                        ids.Add(reader.GetString(0));
                }
                return ids;
            }
        }
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

    public sealed class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>A context that has EF migrations, so a migrate process runs them rather than creating its tables.</summary>
    public sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(Schema);
            modelBuilder.Entity<Widget>(e =>
            {
                e.ToTable("widgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
        }
    }

    /// <summary>WidgetContext's one migration, written by hand.</summary>
    [DbContext(typeof(WidgetContext))]
    [Migration(Id)]
    public sealed class CreateWidgets : Migration
    {
        public const string Id = "20261116000000_CreateWidgets";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(Schema);
            migrationBuilder.CreateTable(
                name: "widgets",
                schema: Schema,
                columns: table => new
                {
                    id = table
                        .Column<int>(nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    name = table.Column<string>(nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_widgets", x => x.id)
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropTable(name: "widgets", schema: Schema);
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
