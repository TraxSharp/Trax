using Microsoft.EntityFrameworkCore;
using Npgsql;

// This SetUpFixture lives in the ROOT test namespace on purpose: a SetUpFixture only wraps its own
// namespace and descendants, never its parent. Placed in a child namespace (e.g. `.Fixtures`) that holds
// no tests, its OneTimeSetUp/OneTimeTearDown would run back-to-back and drop the database out from under
// the real tests in the parent namespace.
namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Creates a throwaway database on the local Trax Postgres (the docker-compose <c>trax_database</c>) once
/// for the whole assembly, builds the snapshot tables via <c>EnsureCreated</c>, and drops it at the end.
/// A fresh database keeps <c>EnsureCreated</c> honest and isolates these tests from the shared trax tables.
/// Each test uses its own <c>DbContext</c> so it hits the DB composite key, not the EF identity map.
/// </summary>
[SetUpFixture]
public class PostgresSetup
{
    // Connect to the always-present `postgres` maintenance database to create/drop the throwaway one. The
    // local docker-compose and the CI Postgres service disagree on which app databases exist, but every
    // Postgres has `postgres`.
    private const string Maintenance =
        "Host=localhost;Port=5432;Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";
    private const string Database = "trax_statemachine_it";

    public static string ConnectionString { get; } =
        $"Host=localhost;Port=5432;Username=trax;Password=trax123;Database={Database};Include Error Detail=true;Maximum Pool Size=40";

    [OneTimeSetUp]
    public async Task Up()
    {
        await using (var admin = new NpgsqlConnection(Maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {Database}");
        }

        await using var db = new SnapshotDbContext(
            new DbContextOptionsBuilder<SnapshotDbContext>().UseNpgsql(ConnectionString).Options
        );
        await db.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await Exec(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
