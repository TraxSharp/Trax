using DbUp;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Trax.Effect.Data.Sqlite.Utils;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteDatabaseMigratorTests
{
    [Test]
    public async Task Migrate_FreshTempDatabase_AppliesEmbeddedScripts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trax_migrator_ok_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await DatabaseMigrator.Migrate(connectionString);

            File.Exists(dbPath).Should().BeTrue();
            new FileInfo(dbPath).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Test]
    public async Task Migrate_InvalidConnectionString_Rethrows()
    {
        // A path under a non-writable directory triggers an SQLite IO failure during PerformUpgrade.
        // DbUp captures the exception, sets Successful = false, and Migrator rethrows from the
        // !Successful branch — the catch block then logs and rethrows again, covering lines 25, 27-32.
        var invalidPath = "/proc/this-cannot-be-written/migrator-fail.db";
        var connectionString = $"Data Source={invalidPath}";

        var act = async () => await DatabaseMigrator.Migrate(connectionString);

        await act.Should().ThrowAsync<Exception>();
    }

    [Test]
    public void CreateEngineWithEmbeddedScripts_ReturnsEngine()
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"trax_migrator_engine_{Guid.NewGuid():N}.db"
        );
        try
        {
            var engine = DatabaseMigrator.CreateEngineWithEmbeddedScripts($"Data Source={dbPath}");

            engine.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Test]
    public async Task Migrate_AScriptThatFailsPartway_LeavesNothingBehindAndSucceedsWhenRerun()
    {
        // SQLite has no ADD COLUMN IF NOT EXISTS. If 007 added its column and then failed, a
        // script run outside a transaction would leave the column behind unjournaled, and every
        // later startup would fail on "duplicate column name".
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"trax_migrator_atomic_{Guid.NewGuid():N}.db"
        );
        var connectionString = $"Data Source={dbPath};Pooling=False";

        try
        {
            var upTo006 = DeployChanges
                .To.SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(Trax.Effect.Data.Sqlite.AssemblyMarker).Assembly,
                    name => MigrationNumber(name) <= 6
                )
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo006.Successful.Should().BeTrue(upTo006.Error?.ToString());

            // A queued row for 007's backfill to update, and a trigger that makes that update
            // fail after the column has been added.
            await Exec(
                connectionString,
                "INSERT INTO work_queue (external_id, train_name, status) VALUES ('q', 'T', 0);"
                    + "CREATE TRIGGER fail_backfill BEFORE UPDATE ON work_queue "
                    + "BEGIN SELECT RAISE(ABORT, 'backfill failed'); END;"
            );

            var failed = async () => await DatabaseMigrator.Migrate(connectionString);
            await failed.Should().ThrowAsync<Exception>();

            (await ColumnExists(connectionString, "work_queue", "confirmed_at"))
                .Should()
                .BeFalse("the failed script's ALTER TABLE must roll back with it");

            await Exec(connectionString, "DROP TRIGGER fail_backfill;");

            var rerun = async () => await DatabaseMigrator.Migrate(connectionString);
            await rerun.Should().NotThrowAsync();
            (await ColumnExists(connectionString, "work_queue", "confirmed_at")).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int MigrationNumber(string resourceName)
    {
        var file = resourceName[(resourceName.IndexOf(".Migrations.") + ".Migrations.".Length)..];
        return int.Parse(file[..file.IndexOf('_')]);
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExists(
        string connectionString,
        string table,
        string column
    )
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name = $c;";
        command.Parameters.AddWithValue("$c", column);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
