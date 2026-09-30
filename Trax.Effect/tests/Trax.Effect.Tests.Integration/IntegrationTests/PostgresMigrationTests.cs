using DbUp;
using DbUp.Postgresql;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Tests.Integration.Fixtures;

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
        // 047: the runner prunes expired nonces by this, on every sweep.
        "ix_runner_nonce_expires_at",
        // 050: a consumer's lookup of its runs by external id.
        "ix_metadata_external_id",
    ];

    private static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );
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

            var assembly = typeof(Trax.Effect.Data.Postgres.Utils.DatabaseMigrator).Assembly;
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
    /// Hosts that start together against a fresh database each run the migration at registration.
    /// Without a lock they ran the same DDL side by side and one of them crashed on an object the
    /// other had just created.
    /// </summary>
    [Test]
    public async Task Concurrent_migrations_of_an_empty_database_all_succeed()
    {
        await WithEmptyDatabase(async connectionString =>
        {
            var hosts = Enumerable
                .Range(0, 4)
                .Select(_ => Task.Run(() => DatabaseMigrator.Migrate(connectionString)));

            var act = () => Task.WhenAll(hosts);

            await act.Should()
                .NotThrowAsync("the advisory lock makes the second host wait for the first");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var journaled = await ExternalIds(
                connection,
                "SELECT scriptname FROM trax.migrations ORDER BY scriptname;"
            );
            journaled.Should().OnlyHaveUniqueItems("each script is applied once");
            journaled.Should().HaveCount(EmbeddedScripts().Count);
        });
    }

    /// <summary>
    /// The migrator holds its lock on one connection while the scripts run on another. A host whose
    /// connection string caps the pool at one connection must still migrate: the migration's
    /// connections cannot come from the host's pool.
    /// </summary>
    [Test]
    public async Task A_connection_string_with_a_pool_of_one_still_migrates()
    {
        await WithEmptyDatabase(async connectionString =>
        {
            var pooledToOne = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MaxPoolSize = 1,
                Timeout = 5,
            }.ConnectionString;

            var act = () => DatabaseMigrator.Migrate(pooledToOne);

            await act.Should().NotThrowAsync("the migration does not borrow from the host's pool");
        });
    }

    /// <summary>
    /// A script that stops partway is not journaled, and runs again from its first statement at the
    /// next start. From 046 on every script has to survive that: this runs each one again over a
    /// database that already has it, which is the harshest partial state there is.
    /// </summary>
    [Test]
    public async Task Every_script_from_046_on_runs_again_over_its_own_result()
    {
        await WithEmptyDatabase(async connectionString =>
        {
            await DatabaseMigrator.Migrate(connectionString);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            foreach (var (name, script) in EmbeddedScripts().Where(s => Number(s.Name) >= 46))
            {
                var statements = new PostgresqlConnectionManager(connectionString)
                    .SplitScriptIntoCommands(script)
                    .ToList();

                foreach (var statement in statements)
                {
                    var act = () => Exec(connection, statement);
                    await act.Should()
                        .NotThrowAsync(
                            $"{name} must be safe to run again, and this failed:\n{statement}"
                        );
                }
            }
        });
    }

    /// <summary>
    /// Creates a throwaway database migrated to 040, runs <paramref name="test"/> against it, and
    /// drops it.
    /// </summary>
    private static Task WithDatabaseMigratedTo040(Func<string, Task> test) =>
        WithDatabaseMigratedTo(40, test);

    /// <summary>
    /// Creates a throwaway database migrated through script <paramref name="last"/>, runs
    /// <paramref name="test"/> against it, and drops it.
    /// </summary>
    private static Task WithDatabaseMigratedTo(int last, Func<string, Task> test) =>
        WithEmptyDatabase(async connectionString =>
        {
            await using (var setup = new NpgsqlConnection(connectionString))
            {
                await setup.OpenAsync();
                await Exec(setup, "CREATE SCHEMA IF NOT EXISTS trax;");
            }

            var upTo = DeployChanges
                .To.PostgresqlDatabase(connectionString)
                .JournalToPostgresqlTable("trax", "migrations")
                .WithScriptsEmbeddedInAssembly(
                    typeof(Trax.Effect.Data.Postgres.Utils.DatabaseMigrator).Assembly,
                    name => MigrationNumber(name) <= last
                )
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo.Successful.Should().BeTrue(upTo.Error?.ToString());

            await test(connectionString);
        });

    /// <summary>
    /// A consumer correlating its records with runs looks them up by external id. Without an index
    /// every lookup read the whole metadata table.
    /// </summary>
    [Test]
    public async Task A_lookup_by_external_id_reads_an_index()
    {
        var connectionString = GetConnectionString();
        await DatabaseMigrator.Migrate(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Exec(connection, "SET LOCAL enable_seqscan = off;");

        var plan = await ExternalIds(
            connection,
            "EXPLAIN SELECT 1 FROM trax.metadata WHERE external_id = 'abcdefabcdefabcdefabcdefabcdefab';"
        );

        string.Join('\n', plan)
            .Should()
            .Contain("ix_metadata_external_id", "the lookup must not be a pass over every run");
    }

    /// <summary>
    /// A <c>CREATE INDEX CONCURRENTLY</c> that fails leaves its index behind, marked invalid, and
    /// <c>IF NOT EXISTS</c> would then skip it at every later start. The migrator drops the
    /// leftover, so the script builds it properly.
    /// </summary>
    [Test]
    public async Task An_index_left_invalid_by_an_interrupted_build_is_built_again()
    {
        await WithDatabaseMigratedTo(
            45,
            async connectionString =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // Two queued entries under one subject make a unique build of 046's index fail
                // partway, which is the one way to leave an invalid index behind without killing
                // a backend.
                await Exec(
                    connection,
                    "INSERT INTO trax.work_queue (external_id, train_name, subject_key) VALUES "
                        + "('invalid-a', 'A.Train', 'subject'), ('invalid-b', 'A.Train', 'subject');"
                );
                var interrupted = () =>
                    Exec(
                        connection,
                        "CREATE UNIQUE INDEX CONCURRENTLY ix_work_queue_subject_queued "
                            + "ON trax.work_queue (subject_key);"
                    );
                await interrupted.Should().ThrowAsync<PostgresException>();
                (await IndexState(connection, "ix_work_queue_subject_queued"))
                    .Should()
                    .Be("invalid", "the failed build leaves its index behind");

                await DatabaseMigrator.Migrate(connectionString);

                (await IndexState(connection, "ix_work_queue_subject_queued"))
                    .Should()
                    .Be(
                        "valid, not unique",
                        "the migrator drops the leftover and 046 builds the index it means"
                    );
            }
        );
    }

    private static async Task<string> IndexState(NpgsqlConnection connection, string index)
    {
        var state = await ExternalIds(
            connection,
            "SELECT CASE WHEN NOT i.indisvalid THEN 'invalid' "
                + "WHEN i.indisunique THEN 'valid, unique' ELSE 'valid, not unique' END "
                + "FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid "
                + $"WHERE c.relname = '{index}';"
        );
        return state.SingleOrDefault() ?? "missing";
    }

    /// <summary>
    /// Creates a throwaway, empty database, runs <paramref name="test"/> against it, and drops it.
    /// </summary>
    private static async Task WithEmptyDatabase(Func<string, Task> test)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString())
        {
            Database = $"trax_migration_{Guid.NewGuid():N}",
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
            await test(builder.ConnectionString);
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
        var assembly = typeof(Trax.Effect.Data.Postgres.Utils.DatabaseMigrator).Assembly;
        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("041_work_queue_confirmed_at.sql"));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>The embedded Postgres scripts, by file name, in the order DbUp runs them.</summary>
    private static List<(string Name, string Script)> EmbeddedScripts()
    {
        var assembly = typeof(Trax.Effect.Data.Postgres.Utils.DatabaseMigrator).Assembly;
        return assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return (
                    name[(name.IndexOf(".Migrations.") + ".Migrations.".Length)..],
                    reader.ReadToEnd()
                );
            })
            .ToList();
    }

    private static int Number(string fileName) => int.Parse(fileName[..fileName.IndexOf('_')]);

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
