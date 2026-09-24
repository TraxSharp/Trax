using DbUp;
using DbUp.Postgresql;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Trax.Effect.Data.Postgres.Utils;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// Checks what the shipped Postgres migrations leave behind. The migration 036 indexes bound the
/// cleanup DELETE (foreign-key back-references) and the per-manifest FailedCount subquery, so a
/// missing or misnamed index would silently reintroduce the O(table) behavior. Migration 041 has
/// to leave no dispatchable work_queue row unconfirmed, even one written mid-migration by an older
/// instance, without rewriting the dispatched history.
/// </summary>
[TestFixture]
public class PostgresMigrationTests
{
    private static readonly string[] ExpectedIndexes =
    [
        "ix_metadata_parent_id",
        "ix_work_queue_metadata_id",
        "ix_dead_letter_retry_metadata_id",
        "ix_metadata_manifest_failed",
    ];

    private static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!;
    }

    [Test]
    public async Task Migrate_CreatesFkAndManifestEvalIndexes()
    {
        var connectionString = GetConnectionString();

        // DbUp is journalled, so this is a no-op when the database is already at 036.
        await DatabaseMigrator.Migrate(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'trax';";

        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));

        foreach (var expected in ExpectedIndexes)
            indexes.Should().Contain(expected, $"index '{expected}' should exist after migration");
    }

    /// <summary>
    /// DbUp runs a script without a transaction, so an instance still on the version before
    /// 041 can insert a row between any two of its statements, without naming confirmed_at.
    /// A row left with a NULL confirmed_at reads as staged: the dispatcher skips it and the
    /// stale-staged sweep cancels it, so accepted work is lost. Every such row must end up
    /// confirmed, whichever statement it lands after.
    /// </summary>
    [Test]
    public async Task Migration041_RowsInsertedByAnOlderWriterMidMigration_AllEndConfirmed()
    {
        await WithDatabaseMigratedTo040(async connectionString =>
        {
            var script = await ReadMigration041();

            // Split exactly as production does: DatabaseMigrator hands the connection string to
            // PostgresqlDatabase, which runs each script through this manager's splitter.
            var statements = new PostgresqlConnectionManager(connectionString)
                .SplitScriptIntoCommands(script)
                .ToList();
            statements.Should().HaveCountGreaterThan(1);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var inserted = 0;
            async Task InsertAsOlderWriter() =>
                await Exec(
                    connection,
                    "INSERT INTO trax.work_queue (external_id, train_name, input, input_type_name) "
                        + $"VALUES ('older-writer-{inserted++}', 'Some.Train', NULL, NULL);"
                );

            await InsertAsOlderWriter();
            foreach (var statement in statements)
            {
                await Exec(connection, statement);
                await InsertAsOlderWriter();
            }

            var unconfirmed = await ExternalIds(
                connection,
                "SELECT external_id FROM trax.work_queue WHERE confirmed_at IS NULL ORDER BY id;"
            );

            unconfirmed
                .Should()
                .BeEmpty(
                    "a row an older instance inserts at any point during 041 must end up confirmed, "
                        + "or the dispatcher never claims it and the stale-staged sweep cancels it"
                );
        });
    }

    [Test]
    public async Task Migration041_BackfillsWhatCanStillBeDispatched_AndLeavesDispatchedHistoryAlone()
    {
        await WithDatabaseMigratedTo040(async connectionString =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            async Task Seed(string externalId, string status, string age) =>
                await Exec(
                    connection,
                    "INSERT INTO trax.work_queue "
                        + "(external_id, train_name, input, input_type_name, status, created_at) "
                        + $"VALUES ('{externalId}', 'Some.Train', NULL, NULL, '{status}', "
                        + $"(now() AT TIME ZONE 'utc') - interval '{age}');"
                );

            await Seed("queued-old", "queued", "10 days");
            await Seed("cancelled-old", "cancelled", "10 days");
            // A dispatch that fails is put back to queued without touching confirmed_at, so one
            // in flight across the migration has to come out of it confirmed.
            await Seed("dispatched-recent", "dispatched", "1 hour");
            await Seed("dispatched-old", "dispatched", "10 days");

            var assembly = typeof(Trax.Effect.Data.Postgres.AssemblyMarker).Assembly;
            var to041 = DeployChanges
                .To.PostgresqlDatabase(connectionString)
                .JournalToPostgresqlTable("trax", "migrations")
                .WithScriptsEmbeddedInAssembly(assembly, name => MigrationNumber(name) <= 41)
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            to041.Successful.Should().BeTrue(to041.Error?.ToString());

            var backfilled = await ExternalIds(
                connection,
                "SELECT external_id FROM trax.work_queue "
                    + "WHERE confirmed_at = created_at ORDER BY external_id;"
            );
            var leftNull = await ExternalIds(
                connection,
                "SELECT external_id FROM trax.work_queue WHERE confirmed_at IS NULL "
                    + "ORDER BY external_id;"
            );

            backfilled
                .Should()
                .BeEquivalentTo(
                    ["cancelled-old", "dispatched-recent", "queued-old"],
                    "every row that is not dispatched history is backfilled from its created_at"
                );
            leftNull
                .Should()
                .BeEquivalentTo(
                    ["dispatched-old"],
                    "rewriting every dispatched row cost 17 s and doubled the table on 2M rows, "
                        + "and nothing reads confirmed_at on one"
                );

            var unconfirmedIndex = await ExternalIds(
                connection,
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'trax' "
                    + "AND indexname = 'ix_work_queue_unconfirmed';"
            );
            unconfirmedIndex
                .Should()
                .ContainSingle()
                .Which.Should()
                .Contain(
                    "'queued'",
                    "the sweep only looks for queued rows, and the dispatched history left null "
                        + "must not sit in the index"
                );
        });
    }

    /// <summary>
    /// Creates a throwaway database migrated to 040, runs <paramref name="test"/> against it, and
    /// drops it.
    /// </summary>
    private static async Task WithDatabaseMigratedTo040(Func<string, Task> test)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString())
        {
            Database = $"trax_migration_041_{Guid.NewGuid():N}",
            Pooling = false,
        };
        var database = builder.Database!;
        var maintenance = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        await using (var admin = new NpgsqlConnection(maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"CREATE DATABASE {database}");
        }

        try
        {
            var connectionString = builder.ConnectionString;
            await using (var setup = new NpgsqlConnection(connectionString))
            {
                await setup.OpenAsync();
                await Exec(setup, "CREATE SCHEMA IF NOT EXISTS trax;");
            }

            var upTo040 = DeployChanges
                .To.PostgresqlDatabase(connectionString)
                .JournalToPostgresqlTable("trax", "migrations")
                .WithScriptsEmbeddedInAssembly(
                    typeof(Trax.Effect.Data.Postgres.AssemblyMarker).Assembly,
                    name => MigrationNumber(name) <= 40
                )
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo040.Successful.Should().BeTrue(upTo040.Error?.ToString());

            await test(connectionString);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(maintenance);
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task<string> ReadMigration041()
    {
        var assembly = typeof(Trax.Effect.Data.Postgres.AssemblyMarker).Assembly;
        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("041_work_queue_confirmed_at.sql"));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<List<string>> ExternalIds(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var rows = await command.ExecuteReaderAsync();
        while (await rows.ReadAsync())
            values.Add(rows.GetString(0));
        return values;
    }

    private static int MigrationNumber(string resourceName)
    {
        var file = resourceName[(resourceName.IndexOf(".Migrations.") + ".Migrations.".Length)..];
        return int.Parse(file[..file.IndexOf('_')]);
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
