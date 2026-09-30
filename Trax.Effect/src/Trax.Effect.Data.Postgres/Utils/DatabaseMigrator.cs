using DbUp;
using DbUp.Engine;
using LanguageExt;
using Npgsql;

namespace Trax.Effect.Data.Postgres.Utils;

/// <summary>
/// Applies the embedded Postgres migration scripts, journaled by DbUp in <c>trax.migrations</c>.
/// </summary>
/// <remarks>
/// <para>
/// Scripts run without a transaction, one statement at a time, so a script can build an index
/// <c>CONCURRENTLY</c>. A script that stops partway keeps the statements before the failure and is
/// not journaled, so it runs again from the top at the next start: every script from 046 on is
/// written to be run again safely, which <c>MigrationsIntegrityTests</c> checks.
/// </para>
/// <para>
/// <see cref="Migrate"/> holds a session advisory lock for the whole run, so hosts that start
/// together against one database migrate one after another rather than racing each other's DDL.
/// See <c>docs/adr/0014-postgres-migrations-are-serialized-and-rerunnable.md</c>.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    /// <summary>
    /// The two-key advisory lock the migration holds: a fixed class key and 0. Postgres keeps the
    /// two-key lock space apart from the single-key one, so it cannot collide with the scheduler's
    /// leader lock or a consumer's own single-key locks.
    /// </summary>
    private const string MigrationLockKey = "hashtext('trax_migrations'), 0";

    /// <summary>
    /// Creates a DbUp upgrade engine over the embedded SQL scripts, journaling to
    /// <c>trax.migrations</c> and logging to trace output.
    /// </summary>
    /// <param name="connectionString">The connection string to the PostgreSQL database</param>
    /// <returns>A configured DbUp upgrade engine</returns>
    /// <remarks>
    /// The engine takes no lock and does not pin the session time zone: <see cref="Migrate"/> does
    /// both. Run the engine directly only where nothing else can be migrating the same database.
    /// </remarks>
    public static UpgradeEngine CreateEngineWithEmbeddedScripts(string connectionString) =>
        DeployChanges
            .To.PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable("trax", "migrations")
            .WithScriptsEmbeddedInAssembly(typeof(AssemblyMarker).Assembly)
            .LogToTrace()
            .Build();

    /// <summary>
    /// Migrates the PostgreSQL database to the latest schema version.
    /// </summary>
    /// <param name="connectionString">The connection string to the PostgreSQL database</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="Exception">Thrown if the migration fails</exception>
    /// <remarks>
    /// <para>
    /// Takes a session advisory lock first and holds it until every script has run, so a second
    /// host calling this waits for the first rather than running the same DDL beside it. The wait
    /// has no timeout: a host whose migration hangs holds the others at startup. Inside
    /// the lock it creates the <c>trax</c> schema, drops any index in it left <c>INVALID</c> by an
    /// interrupted <c>CREATE INDEX CONCURRENTLY</c> (so the script that builds it can build it
    /// again), and applies the pending scripts.
    /// </para>
    /// <para>
    /// Every connection it opens has its time zone pinned to UTC, whatever the connection string
    /// or the server says, so a script that reads or writes a wall-clock time means UTC by it.
    /// Those connections are unpooled: the migration needs two at once, and does not take them
    /// from the host's pool.
    /// </para>
    /// <para>
    /// <c>UsePostgres</c> calls this during service registration unless migrations are skipped.
    /// </para>
    /// </remarks>
    public static async Task Migrate(string connectionString)
    {
        // The lock is held on one connection while DbUp runs the scripts on another, so the
        // migration's connections must not come from the host's pool: a host that caps its pool
        // at one connection would otherwise wait on itself until the pool timeout.
        var migrationConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timezone = "UTC",
            Pooling = false,
        }.ConnectionString;

        try
        {
            await using var lockConnection = new NpgsqlConnection(migrationConnectionString);
            await lockConnection.OpenAsync();
            await AcquireMigrationLock(lockConnection);

            try
            {
                await lockConnection.ReloadTypesAsync();
                await Execute(lockConnection, "create schema if not exists trax;");
                await DropInvalidIndexes(lockConnection);

                var result = CreateEngineWithEmbeddedScripts(migrationConnectionString)
                    .PerformUpgrade();

                if (result.Successful == false)
                    result.Error.Rethrow();
            }
            finally
            {
                await Execute(lockConnection, $"SELECT pg_advisory_unlock({MigrationLockKey});");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"Caught Exception ({e.GetType()}) while attempting to migrate Trax.Core database: {e}"
            );
            throw;
        }
    }

    /// <summary>
    /// Drops every <c>INVALID</c> index in the <c>trax</c> schema. A <c>CREATE INDEX CONCURRENTLY</c>
    /// that fails or is interrupted leaves its index behind, invalid, and <c>IF NOT EXISTS</c> then
    /// skips it forever. Under the migration lock nothing else is building an index, so an invalid
    /// one is a leftover. Dropped concurrently, so writers are not blocked.
    /// </summary>
    private static async Task DropInvalidIndexes(NpgsqlConnection connection)
    {
        var invalid = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.relname
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'trax' AND NOT i.indisvalid;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                invalid.Add(reader.GetString(0));
        }

        foreach (var index in invalid)
        {
            var quoted = "\"" + index.Replace("\"", "\"\"") + "\"";
            await Execute(connection, $"DROP INDEX CONCURRENTLY IF EXISTS trax.{quoted};");
        }
    }

    /// <summary>
    /// Takes the migration lock, polling with <c>pg_try_advisory_lock</c> rather than waiting in
    /// <c>pg_advisory_lock</c>.
    /// </summary>
    /// <remarks>
    /// A session blocked in <c>pg_advisory_lock</c> is inside a statement, and so holds a snapshot
    /// for as long as it waits. <c>CREATE INDEX CONCURRENTLY</c> waits for every transaction with
    /// an older snapshot to finish, so the host holding the lock would wait on the host waiting
    /// for it, forever, and Postgres cannot see that as a deadlock. Between two tries the waiter
    /// holds no snapshot, so the build finishes and the lock is released.
    /// </remarks>
    private static async Task AcquireMigrationLock(NpgsqlConnection connection)
    {
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock({MigrationLockKey});";
            if ((bool)(await command.ExecuteScalarAsync())!)
                return;

            await Task.Delay(MigrationLockPollInterval);
        }
    }

    private static readonly TimeSpan MigrationLockPollInterval = TimeSpan.FromMilliseconds(200);

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
