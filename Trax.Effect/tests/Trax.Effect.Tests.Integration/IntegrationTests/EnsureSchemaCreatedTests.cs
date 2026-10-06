using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>EnsureSchemaCreatedAsync</c> creates the tables a domain context's model has and the
/// database lacks, leaves the ones it has alone, and lets a statement the database refuses fail
/// the call.
/// </summary>
[TestFixture]
[NonParallelizable]
public class EnsureSchemaCreatedTests
{
    private const string Schema = "ensure_schema_tests";
    private const string CatalogSchema = "ensure_schema_tests_catalog";

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

        await DropBoth();
    }

    [TearDown]
    public async Task DropTheSchemaAfter() => await DropBoth();

    private async Task DropBoth() =>
        await Exec(
            $"DROP SCHEMA IF EXISTS {Schema} CASCADE; DROP SCHEMA IF EXISTS {CatalogSchema} CASCADE;"
        );

    [Test]
    public async Task A_second_run_over_a_created_schema_changes_nothing()
    {
        await using var services = Services();

        await services.EnsureSchemaCreatedAsync<OneTableContext>();
        await Exec($"INSERT INTO {Schema}.gadgets (name) VALUES ('kept');");

        var again = async () => await services.EnsureSchemaCreatedAsync<OneTableContext>();

        await again.Should().NotThrowAsync();
        (await Names("gadgets")).Should().Equal("kept");
    }

    [Test]
    public async Task A_table_added_to_the_model_later_is_created_and_the_old_one_kept()
    {
        await using var services = Services();
        await services.EnsureSchemaCreatedAsync<OneTableContext>();
        await Exec($"INSERT INTO {Schema}.gadgets (name) VALUES ('kept');");

        await services.EnsureSchemaCreatedAsync<TwoTableContext>();

        (await Names("gadgets")).Should().Equal("kept");
        await Exec($"INSERT INTO {Schema}.gizmos (name) VALUES ('new');");
        (await Names("gizmos")).Should().Equal("new");
    }

    [Test]
    public async Task A_create_statement_the_database_refuses_fails_the_call_and_creates_nothing()
    {
        await using var services = Services();

        // A composite type takes the name the second table's own row type needs, so its CREATE
        // TABLE is refused. The table before it in the same run must not be left behind.
        await Exec($"CREATE SCHEMA {Schema}; CREATE TYPE {Schema}.gizmos AS (a int);");

        var create = async () => await services.EnsureSchemaCreatedAsync<TwoTableContext>();

        await create.Should().ThrowAsync<PostgresException>();
        (await TableExists("gadgets")).Should().BeFalse("the statements run in one transaction");
    }

    [Test]
    public async Task A_table_mapped_from_another_schema_does_not_make_the_first_creation_skip_the_sequences()
    {
        await using var services = Services();

        // The catalog's table exists before the lending context is created for the first time.
        await services.EnsureSchemaCreatedAsync<CatalogContext>();
        await services.EnsureSchemaCreatedAsync<LendingContext>();

        (await Scalar($"SELECT nextval('{Schema}.loan_seq');")).Should().Be(1L);
        await Exec($"INSERT INTO {Schema}.loans (title) VALUES ('first');");
    }

    [Test]
    public async Task A_sequence_added_to_the_model_later_is_created()
    {
        await using var services = Services();
        await services.EnsureSchemaCreatedAsync<OneTableContext>();
        await Exec($"INSERT INTO {Schema}.gadgets (name) VALUES ('kept');");

        await services.EnsureSchemaCreatedAsync<SequenceContext>();

        (await Names("gadgets")).Should().Equal("kept");
        (await Scalar($"SELECT nextval('{Schema}.gadget_seq');")).Should().Be(1L);
    }

    [Test]
    public async Task Hosts_that_start_together_against_a_fresh_schema_all_start()
    {
        // Each host has its own service provider, and so its own connections. Several rounds,
        // because a race lost once in a while is the failure this guards against.
        for (var round = 0; round < 5; round++)
        {
            await DropBoth();
            var hosts = Enumerable.Range(0, 6).Select(_ => Services()).ToList();
            try
            {
                var starts = hosts.Select(host =>
                    Task.Run(() => host.EnsureSchemaCreatedAsync<TwoTableContext>())
                );

                var start = async () => await Task.WhenAll(starts);

                await start.Should().NotThrowAsync($"round {round}");
            }
            finally
            {
                foreach (var host in hosts)
                    await host.DisposeAsync();
            }
        }

        await Exec($"INSERT INTO {Schema}.gizmos (name) VALUES ('one');");
        (await Names("gizmos")).Should().Equal("one");
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDomainDataContext<OneTableContext, OneTableContext>(o =>
            o.UseNpgsql(_connectionString)
        );
        services.AddDomainDataContext<TwoTableContext, TwoTableContext>(o =>
            o.UseNpgsql(_connectionString)
        );
        services.AddDomainDataContext<CatalogContext, CatalogContext>(o =>
            o.UseNpgsql(_connectionString)
        );
        services.AddDomainDataContext<LendingContext, LendingContext>(o =>
            o.UseNpgsql(_connectionString)
        );
        services.AddDomainDataContext<SequenceContext, SequenceContext>(o =>
            o.UseNpgsql(_connectionString)
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

    private async Task<List<string>> Names(string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT name FROM {Schema}.{table} ORDER BY id;",
            connection
        );
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<bool> TableExists(string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables "
                + "WHERE table_schema = @schema AND table_name = @table);",
            connection
        );
        command.Parameters.AddWithValue("schema", Schema);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public sealed class OtherRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public sealed class OneTableContext(DbContextOptions<OneTableContext> options)
        : DomainDataContext<OneTableContext>(options)
    {
        protected override string Schema => EnsureSchemaCreatedTests.Schema;

        protected override void ConfigureModel(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Row>(e =>
            {
                e.ToTable("gadgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
    }

    public sealed class TwoTableContext(DbContextOptions<TwoTableContext> options)
        : DomainDataContext<TwoTableContext>(options)
    {
        protected override string Schema => EnsureSchemaCreatedTests.Schema;

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(e =>
            {
                e.ToTable("gadgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
                e.HasIndex(r => r.Name);
            });
            modelBuilder.Entity<OtherRow>(e =>
            {
                e.ToTable("gizmos");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
        }
    }

    public sealed class Book
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;

        /// <summary>The documented cross-schema read: the catalog owns the table.</summary>
        public static void OnCrossSchemaModelCreating(ModelBuilder modelBuilder, string schema) =>
            modelBuilder.Entity<Book>(e =>
            {
                e.ToTable("books", schema);
                e.Property(b => b.Id).HasColumnName("id");
                e.Property(b => b.Title).HasColumnName("title");
            });
    }

    public sealed class Loan
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
    }

    public sealed class CatalogContext(DbContextOptions<CatalogContext> options)
        : DomainDataContext<CatalogContext>(options)
    {
        protected override string Schema => CatalogSchema;

        protected override void ConfigureModel(ModelBuilder modelBuilder) =>
            Book.OnCrossSchemaModelCreating(modelBuilder, CatalogSchema);
    }

    public sealed class LendingContext(DbContextOptions<LendingContext> options)
        : DomainDataContext<LendingContext>(options)
    {
        protected override string Schema => EnsureSchemaCreatedTests.Schema;

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>("loan_seq");
            modelBuilder.Entity<Loan>(e =>
            {
                e.ToTable("loans");
                e.Property(l => l.Id).HasColumnName("id");
                e.Property(l => l.Title).HasColumnName("title");
            });
            Book.OnCrossSchemaModelCreating(modelBuilder, CatalogSchema);
        }
    }

    public sealed class SequenceContext(DbContextOptions<SequenceContext> options)
        : DomainDataContext<SequenceContext>(options)
    {
        protected override string Schema => EnsureSchemaCreatedTests.Schema;

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>("gadget_seq");
            modelBuilder.Entity<Row>(e =>
            {
                e.ToTable("gadgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
        }
    }
}
