using System.Text.RegularExpressions;
using Npgsql;

namespace Trax.Api.Tests.Stress.Utils;

/// <summary>
/// Row counts for a stress run. Defaults target genuine millions; override any value
/// with the matching <c>TRAX_STRESS_*</c> environment variable for smaller/larger runs.
/// </summary>
/// <remarks>
/// FK-bearing columns are wired by modular arithmetic against these counts (e.g. a
/// metadata row's <c>manifest_id</c> is <c>1 + (g % Manifests)</c>), which is valid
/// because every table is truncated with <c>RESTART IDENTITY</c> before seeding, so
/// identity/serial ids run 1..N with no gaps.
/// </remarks>
public sealed record StressProfile(
    long Metadata,
    long Log,
    long WorkQueue,
    long DeadLetter,
    int Manifests,
    int Groups,
    int TrainNames,
    int PersistedOperations,
    long Decisions
)
{
    public static StressProfile FromEnvironment() =>
        new(
            Metadata: EnvLong("TRAX_STRESS_METADATA", 3_000_000),
            Log: EnvLong("TRAX_STRESS_LOG", 3_000_000),
            WorkQueue: EnvLong("TRAX_STRESS_WORKQUEUE", 1_500_000),
            DeadLetter: EnvLong("TRAX_STRESS_DEADLETTER", 1_000_000),
            Manifests: (int)EnvLong("TRAX_STRESS_MANIFEST", 5_000),
            Groups: (int)EnvLong("TRAX_STRESS_GROUP", 200),
            TrainNames: (int)EnvLong("TRAX_STRESS_NAMES", 50),
            // Persisted operations grow with releases, not with traffic, so they get a large
            // catalog rather than millions: many tenants' documents, most of them retired.
            PersistedOperations: (int)EnvLong("TRAX_STRESS_PERSISTED_OPS", 100_000),
            // Only runs whose trains ask deciders record decisions, a few each, so the table is a
            // fraction of the run table's size.
            Decisions: EnvLong("TRAX_STRESS_DECISIONS", 1_000_000)
        );

    private static long EnvLong(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}

/// <summary>
/// Seeds the admin-facing tables (manifest_group, manifest, metadata, dead_letter,
/// work_queue, log, persisted_operation and its history) with millions of rows using server-side <c>generate_series</c> inserts.
/// This is orders of magnitude faster than EF <c>SaveChanges</c> loops: the data never
/// leaves Postgres. Idempotent — re-running with the same profile skips reseeding.
/// </summary>
public static class BulkSeeder
{
    private const long ChunkSize = 500_000;

    // Distributes start_time / created_at over the last 14 days by minute. 20160 = 14*24*60.
    // Guarantees dense coverage of every dashboard window (last hour, last 24h, last 7d).
    private const int MinuteSpread = 20160;

    // Number of distinct host instances stamped across the metadata, so the cluster rollup groups
    // millions of rows into a handful of hosts.
    private const int HostInstances = 4;

    public static void EnsureDatabaseExists(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        if (!Regex.IsMatch(database, "^[a-z_][a-z0-9_]*$"))
            throw new ArgumentException(
                $"Database name '{database}' must be a snake_case ASCII identifier.",
                nameof(database)
            );

        // Maintenance connection on the same server + credentials as the target, switched to the
        // always-present "postgres" database, so CREATE DATABASE works regardless of which host
        // TRAX_STRESS_CONNECTION points at (not just localhost:5432).
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
            Timeout = 30,
        }.ConnectionString;
        using var connection = new NpgsqlConnection(maintenance);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{database}\"";
        try
        {
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04")
        {
            // Already exists — idempotent no-op.
        }
    }

    /// <summary>
    /// Seeds all tables to the given profile. Skips work entirely if the metadata, log and
    /// persisted_operation tables already hold at least 95% of their target counts (so
    /// repeated runs are instant).
    /// </summary>
    public static async Task SeedAsync(
        string connectionString,
        StressProfile profile,
        Action<string> log,
        CancellationToken ct = default
    )
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        if (await AlreadySeeded(conn, profile, ct))
        {
            log(
                $"Already seeded (metadata≈{profile.Metadata:N0}, log≈{profile.Log:N0}); skipping."
            );
            // A seed from before decisions were seeded gets them without reseeding the rest.
            await SeedDecisionsAsync(conn, profile, log, ct);
            return;
        }

        log("Truncating tables (RESTART IDENTITY CASCADE)...");
        await Exec(
            conn,
            "TRUNCATE trax.log, trax.work_queue, trax.dead_letter, trax.metadata, "
                + "trax.manifest, trax.manifest_group, trax.persisted_operation, "
                + "trax.persisted_operation_history RESTART IDENTITY CASCADE",
            ct
        );

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── manifest_group (serial id 1..Groups) ─────────────────────────────
        log($"Seeding {profile.Groups:N0} manifest_group...");
        await SeedTable(
            conn,
            profile.Groups,
            "INSERT INTO trax.manifest_group (name) "
                + "SELECT 'stress-group-' || g FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── manifest (identity id 1..Manifests) ──────────────────────────────
        log($"Seeding {profile.Manifests:N0} manifest...");
        await SeedTable(
            conn,
            profile.Manifests,
            "INSERT INTO trax.manifest (external_id, name, manifest_group_id, schedule_type) "
                + "SELECT 'stress-manifest-' || g, "
                + $"       '{TrainName}' || (g % {profile.TrainNames}), "
                + $"       1 + (g % {profile.Groups}), "
                + "       (ARRAY['cron','interval','none','on_demand','once']::trax.schedule_type[])[1 + (g % 5)] "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── metadata (identity id 1..Metadata) ───────────────────────────────
        // 9-way state mix: 4 completed, 2 failed, in_progress, pending, cancelled.
        // Terminal states (completed/failed/cancelled) get an end_time; the rest are null.
        // A failed run is classified transient, conflict or permanent by (g / 9) % 3, so the
        // failureClass filter has a real share of the table to count; every other run keeps the
        // column's default, unclassified. A failed run names the junction it failed in and a
        // reason: see FailureReasonSql for the terms the failure text searches find.
        log($"Seeding {profile.Metadata:N0} metadata...");
        await SeedTable(
            conn,
            profile.Metadata,
            "INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, manifest_id, parent_id, host_instance_id, host_name, host_environment, failure_class, failure_junction, failure_reason) "
                + "SELECT lpad(g::text, 32, '0'), "
                + $"       '{TrainName}' || (g % {profile.TrainNames}), "
                + "       (ARRAY['completed','completed','completed','completed','failed','failed','in_progress','pending','cancelled']::trax.train_state[])[1 + (g % 9)], "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute'), "
                + "       CASE WHEN (g % 9) IN (0,1,2,3,4,5,8) "
                + $"            THEN now() - ((g % {MinuteSpread}) * interval '1 minute') + interval '30 seconds' "
                + "            ELSE NULL END, "
                + $"       1 + (g % {profile.Manifests}), "
                // ~1% of rows are children of metadata id 1, so the parent/child query has a
                // large but partial-indexed set to page (ix_metadata_parent_id).
                + "       CASE WHEN g > 1 AND (g % 100) = 0 THEN 1 ELSE NULL END, "
                // A few host instances so the cluster (hosts) rollup aggregates the whole table
                // into a handful of groups — the real shape of that endpoint at scale.
                + $"       'stress-instance-' || (g % {HostInstances}), "
                + $"       'stress-host-' || (g % {HostInstances}), "
                + "       'Production', "
                + "       CASE WHEN (g % 9) IN (4,5) "
                + "            THEN (ARRAY['transient','conflict','permanent']::trax.failure_class[])[1 + ((g / 9) % 3)] "
                + "            ELSE 'unclassified'::trax.failure_class END, "
                + "       CASE WHEN (g % 9) IN (4,5) "
                + $"            THEN '{FailureJunctionPrefix}' || ((g / 9) % {FailureJunctions}) END, "
                + "       CASE WHEN (g % 9) IN (4,5) THEN "
                + FailureReasonSql
                + " END "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── dead_letter (identity id 1..DeadLetter) ──────────────────────────
        log($"Seeding {profile.DeadLetter:N0} dead_letter...");
        await SeedTable(
            conn,
            profile.DeadLetter,
            "INSERT INTO trax.dead_letter (manifest_id, dead_lettered_at, status, reason, retry_count_at_dead_letter) "
                + $"SELECT 1 + (g % {profile.Manifests}), "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute'), "
                + "       (ARRAY['awaiting_intervention','awaiting_intervention','retried','acknowledged']::trax.dead_letter_status[])[1 + (g % 4)], "
                + "       'stress dead letter ' || g, "
                + "       (g % 5) "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── work_queue (serial id 1..WorkQueue) ──────────────────────────────
        // status mix: dispatched, dispatched, cancelled, queued. Queued rows get a NULL
        // manifest_id to avoid the unique partial index ix_work_queue_unique_queued_manifest.
        log($"Seeding {profile.WorkQueue:N0} work_queue...");
        await SeedTable(
            conn,
            profile.WorkQueue,
            "INSERT INTO trax.work_queue (external_id, train_name, status, created_at, priority, dispatch_attempts, manifest_id) "
                + "SELECT 'wq-' || g, "
                + $"       '{TrainName}' || (g % {profile.TrainNames}), "
                + "       (ARRAY['dispatched','dispatched','cancelled','queued']::trax.work_queue_status[])[1 + (g % 4)], "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute'), "
                + "       (g % 32), "
                + "       (g % 5), "
                + $"       CASE WHEN (g % 4) = 3 THEN NULL ELSE 1 + (g % {profile.Manifests}) END "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── log (identity id 1..Log) ─────────────────────────────────────────
        // Concentrate logs onto ~1/1000 of the metadata ids so a metadata_id filter
        // returns a realistic page (a chatty train), not ~1 row under a uniform spread.
        var logMetaSpread = (int)Math.Max(1, Math.Min(profile.Metadata / 1000, profile.Metadata));
        log($"Seeding {profile.Log:N0} log (metadata_id spread over {logMetaSpread:N0} ids)...");
        await SeedTable(
            conn,
            profile.Log,
            "INSERT INTO trax.log (metadata_id, event_id, level, message, category) "
                + $"SELECT 1 + (g % {logMetaSpread}), "
                + "       (g % 1000), "
                + "       (ARRAY['information','information','information','information','warning','error','debug','trace']::trax.log_level[])[1 + (g % 8)], "
                + "       'stress log message ' || g, "
                + $"       'Trax.Stress.Category' || (g % 20) "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        // ── persisted_operation (+ one history row each) ─────────────────────
        // Ten tenants plus the no-tenant set, ids in the name_vN form HotChocolate accepts as a document id, one in
        // four retired, updated_at spread like the other tables so the list's newest-first
        // order has a real sort to do.
        log($"Seeding {profile.PersistedOperations:N0} persisted_operation...");
        await SeedTable(
            conn,
            profile.PersistedOperations,
            "INSERT INTO trax.persisted_operation (tenant_key, id, operation_name, version, document, "
                + "shape_fingerprint, is_active, description, created_at, updated_at) "
                + "SELECT CASE WHEN g % 11 = 0 THEN '' ELSE 'tenant-' || (g % 11) END, "
                + "       'StressOp' || g || '_v1', 'StressOp' || g, 1, "
                + "       'query StressOp' || g || ' { operations { health { status } } }', "
                + "       md5(g::text), (g % 4) <> 0, 'stress persisted operation ' || g, "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute'), "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute') "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );
        await SeedTable(
            conn,
            profile.PersistedOperations,
            "INSERT INTO trax.persisted_operation_history (tenant_key, id, document, "
                + "shape_fingerprint, change_type, changed_at, changed_reason) "
                + "SELECT CASE WHEN g % 11 = 0 THEN '' ELSE 'tenant-' || (g % 11) END, "
                + "       'StressOp' || g || '_v1', "
                + "       'query StressOp' || g || ' { operations { health { status } } }', "
                + "       md5(g::text), 'Upsert', "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute'), NULL "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );

        await SeedDecisionsAsync(conn, profile, log, ct);

        // VACUUM (not just ANALYZE) so the visibility map is set and the metrics
        // covering indexes serve heap-free Index Only Scans immediately, the way
        // autovacuum keeps them in production. PARALLEL 0 keeps VACUUM off the shared-
        // memory segment so it works regardless of the container's /dev/shm size.
        log($"Inserts done in {sw.Elapsed.TotalSeconds:F0}s. Running VACUUM ANALYZE...");
        await Exec(
            conn,
            "VACUUM (ANALYZE, PARALLEL 0) trax.manifest_group, trax.manifest, trax.metadata",
            ct
        );
        await Exec(
            conn,
            "VACUUM (ANALYZE, PARALLEL 0) trax.dead_letter, trax.work_queue, trax.log, "
                + "trax.persisted_operation, trax.persisted_operation_history",
            ct
        );
        log($"Seed complete in {sw.Elapsed.TotalSeconds:F0}s.");
    }

    private const string TrainName = "Trax.Stress.Trains.IStressTrain";

    /// <summary>A failed run's junction is this followed by one of <see cref="FailureJunctions"/> numbers.</summary>
    public const string FailureJunctionPrefix = "StressJunction";

    /// <summary>How many junctions the failed runs are spread across, evenly.</summary>
    public const int FailureJunctions = 20;

    /// <summary>The run id at or below which a failed run's reason is the old-only one.</summary>
    public const long OldFailureIds = 300_000;

    /// <summary>
    /// The failure reason of the <c>g</c>th run, when it failed. One run in 99,999 (every one of
    /// them failed) carries the rare reason, which holds a <c>%</c> and an <c>_</c>; the failed
    /// runs among the oldest <see cref="OldFailureIds"/> carry the old-only reason; every other
    /// failed run, about two in nine of the table, carries the common one.
    /// </summary>
    private static readonly string FailureReasonSql =
        "CASE WHEN g % 99999 = 4 THEN 'Quota exceeded: 100% of tenant_limit used by order ' || g "
        + $"WHEN g <= {OldFailureIds} THEN 'Legacy schema mismatch reading order ' || g "
        + "ELSE 'Connection refused by stress-endpoint-' || (g % 50) || ' after ' || (g % 5) || ' attempts' END";

    /// <summary>Found in about two runs in nine, old and new alike.</summary>
    public const string CommonFailureTerm = "connection REFUSED";

    /// <summary>Found in every failed run among the oldest <see cref="OldFailureIds"/>, and nowhere else.</summary>
    public const string OldFailureTerm = "legacy schema";

    /// <summary>Found in a few dozen runs at the default profile; its <c>%</c> and <c>_</c> must match themselves.</summary>
    public const string RareFailureTerm = "100% of tenant_limit";

    /// <summary>The run the seed gives a long decision history: one question asked in a loop.</summary>
    public const long ChattyDecisionRun = 2;

    /// <summary>How many decisions <see cref="ChattyDecisionRun"/> records.</summary>
    public const int ChattyDecisionCount = 2_000;

    /// <summary>How many decisions each of the other deciding runs records.</summary>
    public const int DecisionsPerRun = 4;

    /// <summary>
    /// The run the <paramref name="g"/>th decision of the bulk seed belongs to (1-based): every
    /// third run from id 1 asks <see cref="DecisionsPerRun"/> questions, so
    /// <see cref="ChattyDecisionRun"/> is never one of them.
    /// </summary>
    public static long DecisionRunOf(long g) => 1 + (g - 1) / DecisionsPerRun * 3;

    /// <summary>
    /// Seeds <c>trax.decision</c> when it holds fewer rows than the profile asks for: the bulk
    /// spread (see <see cref="DecisionRunOf"/>) and <see cref="ChattyDecisionRun"/>'s history.
    /// Each row looks like one <c>AddDecisionRecording</c> writes: a choice question, its answer,
    /// the track it routed and a keyed state hash; one in ten is a replayed answer.
    /// </summary>
    private static async Task SeedDecisionsAsync(
        NpgsqlConnection conn,
        StressProfile profile,
        Action<string> log,
        CancellationToken ct
    )
    {
        var bulk = Math.Min(profile.Decisions, (profile.Metadata - 1) / 3 * DecisionsPerRun);
        var existing = await ScalarLong(conn, "SELECT count(*) FROM trax.decision", ct);
        if (existing >= (bulk + ChattyDecisionCount) * 0.95)
            return;

        log($"Seeding {bulk + ChattyDecisionCount:N0} decision...");
        await Exec(conn, "TRUNCATE trax.decision RESTART IDENTITY", ct);
        await SeedTable(
            conn,
            bulk,
            "INSERT INTO trax.decision (metadata_id, question_key, occurrence, fingerprint, kind, "
                + "question, answer, model, decider, replayed, routes, state_hash, decided_at) "
                + $"SELECT 1 + ((g - 1) / {DecisionsPerRun}) * 3, "
                + $"       'Stress.Question' || ((g - 1) % {DecisionsPerRun}), 0, md5(g::text) || md5(g::text), 'choice', "
                + "       jsonb_build_object('instructions', 'Pick a track', 'options', jsonb_build_array('Express', 'Standard')), "
                + "       to_jsonb((ARRAY['Express','Standard'])[1 + (g % 2)]), "
                + "       CASE WHEN g % 10 = 0 THEN NULL ELSE 'stress-model' END, "
                + "       CASE WHEN g % 10 = 0 THEN NULL ELSE 'Trax.Stress.Deciders.StressDecider' END, "
                + "       g % 10 = 0, "
                + "       jsonb_build_array(jsonb_build_object('track', (ARRAY['Express','Standard'])[1 + (g % 2)], 'fallback_reason', NULL)), "
                + "       'k1:' || md5(g::text) || md5(g::text), "
                + $"       now() - ((g % {MinuteSpread}) * interval '1 minute') "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );
        await SeedTable(
            conn,
            ChattyDecisionCount,
            "INSERT INTO trax.decision (metadata_id, question_key, occurrence, fingerprint, kind, "
                + "question, answer, model, decider, replayed, routes, state_hash, decided_at) "
                + $"SELECT {ChattyDecisionRun}, 'Stress.Loop', g - 1, md5(g::text) || md5(g::text), 'yes_no', "
                + "       jsonb_build_object('instructions', 'Go round again?'), "
                + "       to_jsonb(g % 7 <> 0), 'stress-model', 'Trax.Stress.Deciders.StressDecider', false, "
                + "       jsonb_build_array(jsonb_build_object('track', CASE WHEN g % 7 <> 0 THEN 'Again' ELSE 'Done' END, 'fallback_reason', NULL)), "
                + "       'k1:' || md5(g::text) || md5(g::text), now() - interval '1 hour' + g * interval '1 second' "
                + "FROM generate_series(@lo, @hi) g",
            ct
        );
        await Exec(conn, "VACUUM (ANALYZE, PARALLEL 0) trax.decision", ct);
    }

    private static async Task<bool> AlreadySeeded(
        NpgsqlConnection conn,
        StressProfile profile,
        CancellationToken ct
    )
    {
        var metadata = await ScalarLong(conn, "SELECT count(*) FROM trax.metadata", ct);
        var logs = await ScalarLong(conn, "SELECT count(*) FROM trax.log", ct);
        var persisted = await ScalarLong(conn, "SELECT count(*) FROM trax.persisted_operation", ct);
        // A seed from before failed runs were classified leaves them all unclassified, and one
        // from before they had reasons leaves those null; reseed either.
        var classified = await ScalarLong(
            conn,
            "SELECT count(*) FROM (SELECT 1 FROM trax.metadata "
                + "WHERE failure_class <> 'unclassified' LIMIT 1) c",
            ct
        );
        var reasons = await ScalarLong(
            conn,
            "SELECT count(*) FROM (SELECT 1 FROM trax.metadata "
                + "WHERE failure_reason IS NOT NULL LIMIT 1) r",
            ct
        );
        return metadata >= profile.Metadata * 0.95
            && logs >= profile.Log * 0.95
            && persisted >= profile.PersistedOperations * 0.95
            && classified > 0
            && reasons > 0;
    }

    private static async Task SeedTable(
        NpgsqlConnection conn,
        long total,
        string insertSql,
        CancellationToken ct
    )
    {
        for (long lo = 1; lo <= total; lo += ChunkSize)
        {
            var hi = Math.Min(lo + ChunkSize - 1, total);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = insertSql;
            cmd.CommandTimeout = 1200;
            cmd.Parameters.AddWithValue("lo", lo);
            cmd.Parameters.AddWithValue("hi", hi);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 1200;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarLong(
        NpgsqlConnection conn,
        string sql,
        CancellationToken ct
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 1200;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result);
    }
}
