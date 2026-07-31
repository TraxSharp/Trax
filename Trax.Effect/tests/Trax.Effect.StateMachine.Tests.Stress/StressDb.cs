using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Stress.Fixtures;

// Root namespace on purpose: a SetUpFixture only wraps its own namespace and descendants. Placed in a child
// namespace with no tests, its OneTimeSetUp/TearDown would run back-to-back and drop the database out from
// under the tests. (Same reason as the persistence integration suite's PostgresSetup.)
namespace Trax.Effect.StateMachine.Tests.Stress;

/// <summary>
/// Creates a dedicated throwaway database on the local Trax Postgres for the whole stress run, with a large
/// connection pool, and builds the snapshot tables via <c>EnsureCreated</c>. Skipped entirely unless the
/// suite is enabled, so a normal test run never touches the database. Each operation uses its own
/// <c>DbContext</c> so the tests hit the real database under contention, not the EF identity map.
/// </summary>
[SetUpFixture]
public class StressDb
{
    // The always-present `postgres` maintenance database (see PostgresSetup for why).
    private const string Maintenance =
        "Host=localhost;Port=5432;Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";
    private const string Database = "trax_statemachine_stress";

    public static string ConnectionString { get; } =
        $"Host=localhost;Port=5432;Username=trax;Password=trax123;Database={Database};"
        + "Include Error Detail=true;Maximum Pool Size=64;Timeout=30";

    public static SnapshotDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SnapshotDbContext>().UseNpgsql(ConnectionString).Options);

    public static EfSnapshotStore NewStore() => new(NewContext());

    [OneTimeSetUp]
    public async Task Up()
    {
        if (!StressProfile.Enabled)
            return;

        await using (var admin = new NpgsqlConnection(Maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {Database}");
        }

        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        if (!StressProfile.Enabled)
            return;

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
