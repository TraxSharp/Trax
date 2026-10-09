using AwesomeAssertions;
using DbUp;
using DbUp.Postgresql;
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
        // 054: the metadata cleanup keeps a run that a queued entry or another run replays.
        "ix_work_queue_replay_decisions_of",
        "ix_metadata_replay_decisions_of",
        // 064: a run's decisions read a page at a time, in id order.
        "ix_decision_metadata_id_id",
        // 066: runs searched by the text of their failure, and by the junction they failed in.
        "ix_metadata_failure_reason_trgm",
        "ix_metadata_failure_junction",
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
    /// The model maps <c>log.metadata_id</c> as a plain long, so a NULL in the column broke every
    /// read of a page containing it. 065 turns the NULLs already there into 0 and makes the column
    /// NOT NULL with a default of 0, so a row written without it reads back as the writer's own do.
    /// Rows that already name a run, or 0, are left as they were.
    /// </summary>
    [Test]
    public async Task Migration065_turns_a_null_log_metadata_id_into_0_and_refuses_another() =>
        await WithDatabaseMigratedTo(
            64,
            async connectionString =>
            {
                await using (var before = new NpgsqlConnection(connectionString))
                {
                    await before.OpenAsync();
                    await Exec(
                        before,
                        "INSERT INTO trax.log (event_id, level, message, category) "
                            + "VALUES (0, 'information', 'written before 065', 'Migration')"
                    );
                    // Rows the writer already stored, outside a run and inside one, keep their
                    // value: the upgrade only fills the NULLs.
                    await Exec(
                        before,
                        "INSERT INTO trax.log (metadata_id, event_id, level, message, category) "
                            + "VALUES (0, 0, 'information', 'no run before 065', 'Migration'), "
                            + "(42, 0, 'information', 'a run before 065', 'Migration')"
                    );
                }

                await DatabaseMigrator.Migrate(connectionString);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await Exec(
                    connection,
                    "INSERT INTO trax.log (event_id, level, message, category) "
                        + "VALUES (0, 'information', 'written after 065', 'Migration')"
                );
                (
                    await ExternalIds(
                        connection,
                        "SELECT message || ':' || metadata_id FROM trax.log ORDER BY id"
                    )
                )
                    .Should()
                    .Equal(
                        "written before 065:0",
                        "no run before 065:0",
                        "a run before 065:42",
                        "written after 065:0"
                    );

                var explicitNull = async () =>
                    await Exec(
                        connection,
                        "INSERT INTO trax.log (metadata_id, event_id, level, message, category) "
                            + "VALUES (NULL, 0, 'information', 'null', 'Migration')"
                    );
                await explicitNull.Should().ThrowAsync<PostgresException>();
                (
                    await ExternalIds(
                        connection,
                        "SELECT conname FROM pg_constraint WHERE conname = 'ck_log_metadata_id_not_null'"
                    )
                )
                    .Should()
                    .BeEmpty("the check only stands in for the scan SET NOT NULL would make");
            }
        );

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
    /// A search for text inside a log entry's message or category, written as the scheduler's log
    /// query writes it: lowered, a parameter for the pattern, and an escape clause. The trigram
    /// index only serves an expression it was built over, so this fails when either side changes
    /// shape and the search goes back to reading every entry.
    /// </summary>
    [TestCase("message", "ix_log_message_trgm")]
    [TestCase("category", "ix_log_category_trgm")]
    public async Task A_search_inside_log_text_reads_an_index(string column, string index)
    {
        var connectionString = GetConnectionString();
        await DatabaseMigrator.Migrate(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Exec(connection, "SET LOCAL enable_seqscan = off;");

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"EXPLAIN SELECT count(*) FROM trax.log AS l WHERE lower(l.{column}) LIKE @pattern ESCAPE '\\'";
        command.Parameters.AddWithValue("pattern", "%needle\\_marker%");
        var plan = new List<string>();
        await using (var rows = await command.ExecuteReaderAsync())
            while (await rows.ReadAsync())
                plan.Add(rows.GetString(0));

        string.Join('\n', plan)
            .Should()
            .Contain(index, "a search inside the text must not be a pass over every entry");
    }

    /// <summary>
    /// A search of runs by the text of their failure, written as the executions query writes it:
    /// lowered, a parameter for the pattern, and an escape clause. Like the log's text search,
    /// the trigram index serves only the expression it was built over.
    /// </summary>
    [Test]
    public async Task A_search_inside_a_failure_reason_reads_an_index()
    {
        var plan = await PlanWithoutSeqScan(
            "EXPLAIN SELECT count(*) FROM trax.metadata AS m "
                + "WHERE lower(m.failure_reason) LIKE @value ESCAPE '\\'",
            "%connection\\_refused%"
        );

        plan.Should()
            .Contain(
                "ix_metadata_failure_reason_trgm",
                "a search inside the failure text must not be a pass over every run"
            );
    }

    /// <summary>
    /// The runs that failed in one junction are found through the junction index, not by a pass
    /// over every run. Which way a page is read depends on the table's statistics (the stress
    /// suite measures it at scale); this checks the index exists and serves the comparison.
    /// </summary>
    [Test]
    public async Task A_search_by_failure_junction_reads_an_index()
    {
        var plan = await PlanWithoutSeqScan(
            "EXPLAIN SELECT count(*) FROM trax.metadata WHERE failure_junction = @value",
            "ChargeCardJunction"
        );

        plan.Should()
            .Contain(
                "ix_metadata_failure_junction",
                "a junction's failures must not be found by a pass over every run"
            );
    }

    private static async Task<string> PlanWithoutSeqScan(string explain, string value)
    {
        var connectionString = GetConnectionString();
        await DatabaseMigrator.Migrate(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Exec(connection, "SET LOCAL enable_seqscan = off;");

        await using var command = connection.CreateCommand();
        command.CommandText = explain;
        command.Parameters.AddWithValue("value", value);
        var plan = new List<string>();
        await using (var rows = await command.ExecuteReaderAsync())
            while (await rows.ReadAsync())
                plan.Add(rows.GetString(0));
        return string.Join('\n', plan);
    }

    /// <summary>
    /// A <c>CREATE INDEX CONCURRENTLY</c> that fails leaves its index behind, marked invalid, and
    /// <c>IF NOT EXISTS</c> would then skip it at every later start. The migrator drops the
    /// leftover, so the script builds it properly.
    /// </summary>
    /// <summary>
    /// 070 moves <c>snapshot_draft</c>'s key to a surrogate <c>row_id</c> and adds the owner check
    /// and the identity indexes. A table of drafts may be large and is written all the time, so
    /// none of it may rewrite the table or hold it locked for a scan: the column is added without
    /// a default and filled in batches, NOT NULL and the check are proved by validation that lets
    /// writes continue, and every index is built concurrently. The table keeps its storage, and
    /// every existing draft ends with a key of its own.
    /// </summary>
    [Test]
    public async Task Migration070_keys_existing_drafts_without_rewriting_the_table() =>
        await WithDatabaseMigratedTo(
            69,
            async connectionString =>
            {
                const int drafts = 25_000;
                string storage;
                await using (var before = new NpgsqlConnection(connectionString))
                {
                    await before.OpenAsync();
                    await Exec(
                        before,
                        "INSERT INTO trax.snapshot_draft "
                            + "(id, user_key, machine, version, state, context, concurrency_token, updated_at) "
                            + "SELECT gen_random_uuid(), 'user-' || (n % 7), 'machine-' || (n % 3), 1, 'Draft', "
                            + $"'{{}}'::jsonb, gen_random_uuid(), now() FROM generate_series(1, {drafts}) AS n"
                    );
                    storage = await Filenode(before, "snapshot_draft");
                }

                await DatabaseMigrator.Migrate(connectionString);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                (await Filenode(connection, "snapshot_draft"))
                    .Should()
                    .Be(storage, "no statement of the upgrade rewrote the table");
                (
                    await ExternalIds(
                        connection,
                        "SELECT count(*) || ' ' || count(row_id) || ' ' || count(DISTINCT row_id) "
                            + "FROM trax.snapshot_draft"
                    )
                )
                    .Should()
                    .Equal([$"{drafts} {drafts} {drafts}"], "every draft has a key of its own");
                (
                    await ExternalIds(
                        connection,
                        "SELECT c.conname || ' ' || a.attname FROM pg_constraint c "
                            + "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey) "
                            + "WHERE c.conrelid = 'trax.snapshot_draft'::regclass AND c.contype = 'p'"
                    )
                )
                    .Should()
                    .Equal("pk_snapshot_draft row_id");
                (
                    await ExternalIds(
                        connection,
                        "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'trax' "
                            + "AND table_name = 'snapshot_draft' AND column_name = 'row_id'"
                    )
                )
                    .Should()
                    .Equal("NO");
                (
                    await ExternalIds(
                        connection,
                        "SELECT conname || ' ' || convalidated FROM pg_constraint "
                            + "WHERE conrelid = 'trax.snapshot_draft'::regclass AND contype = 'c'"
                    )
                )
                    .Should()
                    .Equal(
                        ["ck_snapshot_draft_owner true"],
                        "the owner check is proved, and the check that stood in for NOT NULL's scan is gone"
                    );
                foreach (
                    var index in new[]
                    {
                        "ux_snapshot_draft_user_machine_id",
                        "ux_snapshot_draft_system_machine_id",
                        "ux_snapshot_draft_invoke_token",
                    }
                )
                    (await IndexState(connection, index)).Should().Be("valid, unique", index);

                // A host still on the previous version inserts naming neither column, and the row is
                // keyed after every existing one.
                await Exec(
                    connection,
                    "INSERT INTO trax.snapshot_draft "
                        + "(id, user_key, machine, version, state, context, concurrency_token, updated_at) "
                        + "VALUES (gen_random_uuid(), 'late', 'machine-0', 1, 'Draft', '{}'::jsonb, "
                        + "gen_random_uuid(), now())"
                );
                (
                    await ExternalIds(
                        connection,
                        "SELECT (row_id > (SELECT max(row_id) FROM trax.snapshot_draft WHERE user_key <> 'late'))::text "
                            + "|| ' ' || owner_kind FROM trax.snapshot_draft WHERE user_key = 'late'"
                    )
                )
                    .Should()
                    .Equal("true user");
            }
        );

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

    /// <summary>
    /// A <c>timestamp</c> to <c>timestamptz</c> change keeps the table's storage when the session
    /// is in UTC, and rewrites the whole table under <c>ACCESS EXCLUSIVE</c> otherwise (or when the
    /// change carries a <c>USING</c>). 049 must keep the storage and read every stored wall-clock
    /// time as UTC even when it runs from a session whose zone is not UTC, as it does through
    /// <see cref="DatabaseMigrator.CreateEngineWithEmbeddedScripts"/> against a server whose
    /// default zone is not UTC.
    /// </summary>
    [Test]
    public async Task Migration049_keeps_each_tables_storage_and_reads_stored_times_as_utc_from_a_non_utc_session()
    {
        await WithDatabaseMigratedTo(
            48,
            async connectionString =>
            {
                var newYork = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    Timezone = "America/New_York",
                }.ConnectionString;

                await using var connection = new NpgsqlConnection(newYork);
                await connection.OpenAsync();
                await Exec(
                    connection,
                    "INSERT INTO trax.work_queue (external_id, train_name, created_at) "
                        + "VALUES ('before-049', 'A.Train', '2026-01-15 12:00:00');"
                );

                string[] tables = ["work_queue", "manifest_group", "manifest"];
                var before = new Dictionary<string, string>();
                foreach (var table in tables)
                    before[table] = await Filenode(connection, table);

                var result = DeployChanges
                    .To.PostgresqlDatabase(newYork)
                    .JournalToPostgresqlTable("trax", "migrations")
                    .WithScriptsEmbeddedInAssembly(
                        typeof(DatabaseMigrator).Assembly,
                        name => MigrationNumber(name) == 49
                    )
                    .LogToNowhere()
                    .Build()
                    .PerformUpgrade();
                result.Successful.Should().BeTrue(result.Error?.ToString());

                foreach (var table in tables)
                    (await Filenode(connection, table))
                        .Should()
                        .Be(
                            before[table],
                            $"049 must not rewrite trax.{table}: a rewrite holds ACCESS EXCLUSIVE "
                                + "on it for as long as the copy takes"
                        );

                var stored = await ExternalIds(
                    connection,
                    "SELECT to_char(created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') "
                        + "FROM trax.work_queue WHERE external_id = 'before-049';"
                );
                stored
                    .Should()
                    .Equal(
                        ["2026-01-15 12:00:00"],
                        "a stored wall-clock time was written as UTC and must keep meaning UTC"
                    );
            }
        );
    }

    /// <summary>
    /// The migrator runs at startup. A script whose DDL queues behind a transaction another
    /// instance holds open would otherwise queue every later enqueue and dispatch behind itself
    /// for as long as that transaction lasts. It gives up on the lock after a bounded wait and
    /// runs the script again, and after a bounded number of tries fails the start.
    /// </summary>
    [Test]
    public async Task A_script_behind_a_held_lock_times_out_and_the_migrator_gives_up_after_its_tries()
    {
        await WithDatabaseMigratedTo(
            48,
            async connectionString =>
            {
                await using var holder = new NpgsqlConnection(connectionString);
                await holder.OpenAsync();
                await using var held = await holder.BeginTransactionAsync();
                await Exec(holder, "SELECT 1 FROM trax.work_queue LIMIT 1;");

                var policy = new MigrationLockPolicy(
                    TimeSpan.FromMilliseconds(200),
                    Attempts: 3,
                    Backoff: TimeSpan.Zero
                );
                var migrate = () =>
                    DatabaseMigrator.Migrate(connectionString, policy).WaitAsync(Bounded);

                (await migrate.Should().ThrowAsync<PostgresException>())
                    .Which.SqlState.Should()
                    .Be(PostgresErrorCodes.LockNotAvailable);

                await held.RollbackAsync();
            }
        );
    }

    [Test]
    public async Task A_script_behind_a_lock_that_is_released_between_tries_is_run_again_and_succeeds()
    {
        await WithDatabaseMigratedTo(
            48,
            async connectionString =>
            {
                const string application = "trax-migration-retry-test";
                var named = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    ApplicationName = application,
                }.ConnectionString;

                await using var holder = new NpgsqlConnection(connectionString);
                await holder.OpenAsync();
                var held = await holder.BeginTransactionAsync();
                await Exec(holder, "SELECT 1 FROM trax.work_queue LIMIT 1;");

                var policy = new MigrationLockPolicy(
                    TimeSpan.FromMilliseconds(300),
                    Attempts: 50,
                    Backoff: TimeSpan.Zero
                );
                var migration = DatabaseMigrator.Migrate(named, policy);

                // Two distinct waits by the script session prove the first one timed out and the
                // script was run again. Only then is the lock released.
                await using (var observer = new NpgsqlConnection(connectionString))
                {
                    await observer.OpenAsync();
                    var waits = new HashSet<string>();
                    var deadline = DateTime.UtcNow + Bounded;
                    while (waits.Count < 2)
                    {
                        DateTime
                            .UtcNow.Should()
                            .BeBefore(deadline, "the script should have waited on the lock twice");
                        migration.IsCompleted.Should().BeFalse("the lock is still held");
                        foreach (
                            var start in await ExternalIds(
                                observer,
                                "SELECT query_start::text FROM pg_stat_activity "
                                    + $"WHERE application_name = '{application}' "
                                    + "AND wait_event_type = 'Lock';"
                            )
                        )
                            waits.Add(start);
                        await Task.Yield();
                    }
                }

                await held.RollbackAsync();
                await held.DisposeAsync();

                await migration.WaitAsync(Bounded);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                (
                    await ExternalIds(
                        connection,
                        "SELECT scriptname FROM trax.migrations ORDER BY scriptname;"
                    )
                )
                    .Should()
                    .HaveCount(EmbeddedScripts().Count, "every script ran once the lock was free");
            }
        );
    }

    /// <summary>
    /// An invalid index in <c>trax</c> that no script builds is someone else's: a consumer's own
    /// failed build, or the <c>_ccnew</c> copy of a <c>REINDEX CONCURRENTLY</c> still running.
    /// Nothing would build it again, so the migrator must not drop it.
    /// </summary>
    [Test]
    public async Task An_invalid_index_no_script_builds_is_left_alone()
    {
        await WithEmptyDatabase(async connectionString =>
        {
            await DatabaseMigrator.Migrate(connectionString);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await Exec(
                connection,
                "INSERT INTO trax.work_queue (external_id, train_name) VALUES "
                    + "('consumer-a', 'A.Train'), ('consumer-b', 'A.Train');"
            );
            var failed = () =>
                Exec(
                    connection,
                    "CREATE UNIQUE INDEX CONCURRENTLY ix_consumer_work_queue_train "
                        + "ON trax.work_queue (train_name);"
                );
            await failed.Should().ThrowAsync<PostgresException>();

            await DatabaseMigrator.Migrate(connectionString);

            (await IndexState(connection, "ix_consumer_work_queue_train"))
                .Should()
                .Be("invalid", "the migrator drops only what a script builds again");
        });
    }

    [Test]
    public void The_scripts_index_names_include_every_concurrently_built_index()
    {
        DatabaseMigrator
            .ScriptIndexNames.Should()
            .Contain(["ix_work_queue_subject_queued", "ix_metadata_external_id"])
            .And.NotContain(name => name.EndsWith("_ccnew"));
    }

    [Test]
    public void A_duplicate_met_while_building_a_scripts_unique_index_runs_the_scripts_again()
    {
        static PostgresException Error(string sqlState, string? constraint) =>
            new(
                "could not create unique index",
                "ERROR",
                "ERROR",
                sqlState,
                constraintName: constraint
            );

        DatabaseMigrator
            .IsRetryable(
                Error(PostgresErrorCodes.UniqueViolation, "ix_work_queue_unique_queued_replay")
            )
            .Should()
            .BeTrue("migration 062 clears a duplicate queued replay before it builds the index");
        DatabaseMigrator
            .IsRetryable(
                new InvalidOperationException(
                    "wrapped",
                    Error(PostgresErrorCodes.UniqueViolation, "ix_work_queue_unique_queued_replay")
                )
            )
            .Should()
            .BeTrue();
        DatabaseMigrator
            .IsRetryable(Error(PostgresErrorCodes.LockNotAvailable, null))
            .Should()
            .BeTrue();
        DatabaseMigrator
            .IsRetryable(Error(PostgresErrorCodes.UniqueViolation, "some_consumer_index"))
            .Should()
            .BeFalse("only an index a script builds again is retried");
        DatabaseMigrator
            .IsRetryable(Error(PostgresErrorCodes.UniqueViolation, null))
            .Should()
            .BeFalse();
        DatabaseMigrator
            .IsRetryable(Error(PostgresErrorCodes.SyntaxError, null))
            .Should()
            .BeFalse();
    }

    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(60);

    private static async Task<string> Filenode(NpgsqlConnection connection, string table) =>
        (
            await ExternalIds(connection, $"SELECT pg_relation_filenode('trax.{table}')::text;")
        ).Single();

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
