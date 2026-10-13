using Npgsql;
using Trax.Effect.Data.Postgres.Utils;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// A migrated Postgres database of its own, for a test that changes what is database-wide (an
/// <c>ALTER DATABASE ... SET</c>, a trigger's enabled state) and so must not share the run's database. Dropped on
/// dispose, with every setting made on it.
/// </summary>
internal sealed class ScratchDatabase : IAsyncDisposable
{
    private static readonly string Maintenance =
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres";

    private ScratchDatabase(string name)
    {
        Name = name;
        ConnectionString =
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={name};Include Error Detail=true;Maximum Pool Size=10";
    }

    /// <summary>The database's name.</summary>
    public string Name { get; }

    /// <summary>A connection string to it.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates the database and runs the shipped migrations over it.</summary>
    public static async Task<ScratchDatabase> Create()
    {
        var scratch = new ScratchDatabase(
            $"trax_sm_scratch_{Environment.ProcessId}_{Guid.NewGuid():N}"[..48]
        );
        await Admin($"CREATE DATABASE {scratch.Name}");
        await DatabaseMigrator.Migrate(scratch.ConnectionString);
        return scratch;
    }

    /// <summary>Runs <paramref name="sql"/> on a new session of the database.</summary>
    public async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        // A database setting reaches new sessions only, so no pooled one may outlive the change.
        NpgsqlConnection.ClearPool(connection);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
            NpgsqlConnection.ClearPool(connection);
        await Admin($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)");
    }

    private static async Task Admin(string sql)
    {
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync();
    }
}
