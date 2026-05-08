using FluentAssertions;
using Microsoft.Data.Sqlite;
using Trax.Effect.Data.Sqlite.Utils;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteMigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "background_job",
        "dead_letter",
        "log",
        "manifest",
        "manifest_group",
        "metadata",
        "scheduler_config",
        "work_queue",
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "ix_manifest_external_id",
        "manifest_name_idx",
        "manifest_scheduling_idx",
        "ix_manifest_depends_on",
        "ix_manifest_manifest_group_id",
        "ix_metadata_manifest_id",
        "ix_metadata_name_train_state",
        "ix_metadata_train_state_start_time",
        "ix_metadata_start_time_desc",
        "ix_metadata_manifest_id_train_state",
        "ix_metadata_end_time_desc",
        "ix_metadata_host_name",
        "ix_metadata_host_environment",
        "ix_metadata_active_capacity",
        "ix_metadata_cleanup",
        "ix_log_metadata_id",
        "dead_letter_manifest_id_idx",
        "dead_letter_status_idx",
        "dead_letter_dead_lettered_at_idx",
        "ix_work_queue_external_id",
        "ix_work_queue_status",
        "ix_work_queue_manifest_id",
        "ix_work_queue_status_priority",
        "ix_work_queue_unique_queued_manifest",
        "ix_work_queue_scheduled_at",
        "ix_work_queue_manifest_id_status_queued",
        "ix_background_job_unfetched",
    ];

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"trax_migration_test_{Guid.NewGuid():N}.db");

    private static List<string> QueryNames(string dbPath, string type)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type='{type}' ORDER BY name;";
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    #region Migrate

    [Test]
    public void Migrate_FreshDatabase_CreatesAllTables()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            DatabaseMigrator.Migrate($"Data Source={dbPath}").Wait();

            var tables = QueryNames(dbPath, "table");

            foreach (var expected in ExpectedTables)
                tables
                    .Should()
                    .Contain(expected, $"table '{expected}' should exist after migration");

            // DbUp creates a journal table
            tables.Should().Contain("SchemaVersions");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Test]
    public void Migrate_RunTwice_IsIdempotent()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            DatabaseMigrator.Migrate($"Data Source={dbPath}").Wait();

            var act = () => DatabaseMigrator.Migrate($"Data Source={dbPath}").Wait();

            act.Should().NotThrow("running migrations twice should be idempotent");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Test]
    public void Migrate_CreatesAllIndexes()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            DatabaseMigrator.Migrate($"Data Source={dbPath}").Wait();

            var indexes = QueryNames(dbPath, "index");

            foreach (var expected in ExpectedIndexes)
                indexes
                    .Should()
                    .Contain(expected, $"index '{expected}' should exist after migration");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    #endregion
}
