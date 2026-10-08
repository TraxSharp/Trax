using Npgsql;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

// In the root test namespace on purpose: a SetUpFixture wraps only its own namespace and those below it.
namespace Trax.Mediator.Tests.StateMachine.Integration;

/// <summary>
/// Creates a throwaway database on the test Postgres once for the whole assembly, builds the tables with the shipped
/// migrations, and drops it at the end. Its name carries the process id, so two runs sharing a Postgres never drop
/// each other's database.
/// </summary>
[SetUpFixture]
public class PostgresSetup
{
    private static readonly string Maintenance =
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";

    private static readonly string Database = $"trax_mediator_sm_it_{Environment.ProcessId}";

    public static string ConnectionString { get; } =
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={Database};Include Error Detail=true;Maximum Pool Size=40";

    [OneTimeSetUp]
    public async Task Up()
    {
        await using (var admin = new NpgsqlConnection(Maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {Database}");
        }

        await DatabaseMigrator.Migrate(ConnectionString);
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
