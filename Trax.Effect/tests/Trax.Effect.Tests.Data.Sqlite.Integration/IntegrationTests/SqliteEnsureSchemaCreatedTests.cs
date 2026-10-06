using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// On SQLite, which has no schemas, <c>EnsureSchemaCreatedAsync</c> still finds the tables that
/// exist and creates only the rest.
/// </summary>
[TestFixture]
public class SqliteEnsureSchemaCreatedTests
{
    private string _dbPath = null!;

    [SetUp]
    public void NewDatabase() =>
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_ensure_schema_{Guid.NewGuid():N}.db");

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Test]
    public async Task A_second_run_keeps_the_rows_and_a_table_added_later_is_created()
    {
        var services = new ServiceCollection();
        services.AddDomainDataContext<OneTableContext, OneTableContext>(o =>
            o.UseSqlite($"Data Source={_dbPath}")
        );
        services.AddDomainDataContext<TwoTableContext, TwoTableContext>(o =>
            o.UseSqlite($"Data Source={_dbPath}")
        );
        await using var provider = services.BuildServiceProvider();

        await provider.EnsureSchemaCreatedAsync<OneTableContext>();
        Exec("INSERT INTO gadgets (name) VALUES ('kept');");

        await provider.EnsureSchemaCreatedAsync<OneTableContext>();
        await provider.EnsureSchemaCreatedAsync<TwoTableContext>();

        Exec("INSERT INTO gizmos (name) VALUES ('new');");
        Scalar("SELECT group_concat(name) FROM gadgets;").Should().Be("kept");
        Scalar("SELECT group_concat(name) FROM gizmos;").Should().Be("new");
    }

    private void Exec(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
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
        protected override string Schema => "ensure_schema";

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
        protected override string Schema => "ensure_schema";

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(e =>
            {
                e.ToTable("gadgets");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
            modelBuilder.Entity<OtherRow>(e =>
            {
                e.ToTable("gizmos");
                e.Property(r => r.Id).HasColumnName("id");
                e.Property(r => r.Name).HasColumnName("name");
            });
        }
    }
}
