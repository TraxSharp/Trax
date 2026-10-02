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
        "decision",
        "log",
        "manifest",
        "manifest_group",
        "metadata",
        "runner_nonce",
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
        "ix_metadata_parent_id",
        "ix_metadata_manifest_failed",
        "ix_log_metadata_id",
        "dead_letter_manifest_id_idx",
        "dead_letter_status_idx",
        "dead_letter_dead_lettered_at_idx",
        "ix_dead_letter_retry_metadata_id",
        "ix_work_queue_external_id",
        "ix_work_queue_status",
        "ix_work_queue_manifest_id",
        "ix_work_queue_metadata_id",
        "ix_work_queue_status_priority",
        "ix_work_queue_unique_queued_manifest",
        "ix_work_queue_scheduled_at",
        "ix_work_queue_manifest_id_status_queued",
        "ix_work_queue_subject_queued",
        "ix_background_job_unfetched",
        "ix_runner_nonce_expires_at",
        "ix_work_queue_replay_decisions_of",
        "ix_metadata_replay_decisions_of",
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

    /// <summary>
    /// Before 014 the unique queued-per-manifest index compared the status to a label SQLite never
    /// stores, so a database could hold two queued entries for one manifest, and the rebuilt index
    /// cannot be created over them. 014 keeps each manifest's oldest queued entry and cancels the
    /// rest.
    /// </summary>
    [Test]
    public void Migration014_KeepsTheOldestQueuedEntryPerManifest()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var connectionString = $"Data Source={dbPath}";
            var upTo013 = DbUp
                .DeployChanges.To.SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(DatabaseMigrator).Assembly,
                    name =>
                        int.Parse(
                            System
                                .Text.RegularExpressions.Regex.Match(name, @"\.(\d{3})_")
                                .Groups[1]
                                .Value
                        ) <= 13
                )
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo013.Successful.Should().BeTrue(upTo013.Error?.ToString());

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO manifest_group (id, name) VALUES (1, 'g');
                    INSERT INTO manifest (id, external_id, name, manifest_group_id) VALUES (1, 'm', 'M', 1);
                    INSERT INTO work_queue (id, external_id, train_name, status, manifest_id, created_at, dispatch_attempts)
                    VALUES (1, 'first', 'M', 0, 1, datetime('now'), 0),
                           (2, 'second', 'M', 0, 1, datetime('now'), 0),
                           (3, 'done', 'M', 1, 1, datetime('now'), 0);
                    """;
                seed.ExecuteNonQuery();
            }

            DatabaseMigrator.Migrate(connectionString).Wait();

            using var check = new SqliteConnection(connectionString);
            check.Open();
            using var command = check.CreateCommand();
            command.CommandText =
                "SELECT external_id || ':' || status FROM work_queue ORDER BY id;";
            var rows = new List<string>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    rows.Add(reader.GetString(0));

            rows.Should()
                .Equal(
                    ["first:0", "second:2", "done:1"],
                    "the oldest queued entry stays queued, the later one is cancelled, and a "
                        + "dispatched one is left alone"
                );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    #endregion
}
