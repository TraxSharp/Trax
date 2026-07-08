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
    int TrainNames
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
            TrainNames: (int)EnvLong("TRAX_STRESS_NAMES", 50)
        );

    private static long EnvLong(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}

/// <summary>
/// Seeds the six admin-facing tables (manifest_group, manifest, metadata, dead_letter,
/// work_queue, log) with millions of rows using server-side <c>generate_series</c> inserts.
/// This is orders of magnitude faster than EF <c>SaveChanges</c> loops: the data never
/// leaves Postgres. Idempotent — re-running with the same profile skips reseeding.
/// </summary>
public static class BulkSeeder
{
    private const long ChunkSize = 500_000;

    // Distributes start_time / created_at over the last 14 days by minute. 20160 = 14*24*60.
    // Guarantees dense coverage of every dashboard window (last hour, last 24h, last 7d).
    private const int MinuteSpread = 20160;

    public static void EnsureDatabaseExists(string database)
    {
        if (!Regex.IsMatch(database, "^[a-z_][a-z0-9_]*$"))
            throw new ArgumentException(
                $"Database name '{database}' must be a snake_case ASCII identifier.",
                nameof(database)
            );

        const string maintenance =
            "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123;Timeout=30";
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
    /// Seeds all tables to the given profile. Skips work entirely if the metadata and log
    /// tables already hold at least 95% of their target counts (so repeated runs are instant).
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
            return;
        }

        log("Truncating tables (RESTART IDENTITY CASCADE)...");
        await Exec(
            conn,
            "TRUNCATE trax.log, trax.work_queue, trax.dead_letter, trax.metadata, "
                + "trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE",
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
        log($"Seeding {profile.Metadata:N0} metadata...");
        await SeedTable(
            conn,
            profile.Metadata,
            "INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, manifest_id, parent_id) "
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
                + "       CASE WHEN g > 1 AND (g % 100) = 0 THEN 1 ELSE NULL END "
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
                + $"       (now() at time zone 'utc') - ((g % {MinuteSpread}) * interval '1 minute'), "
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
            "VACUUM (ANALYZE, PARALLEL 0) trax.dead_letter, trax.work_queue, trax.log",
            ct
        );
        log($"Seed complete in {sw.Elapsed.TotalSeconds:F0}s.");
    }

    private const string TrainName = "Trax.Stress.Trains.IStressTrain";

    private static async Task<bool> AlreadySeeded(
        NpgsqlConnection conn,
        StressProfile profile,
        CancellationToken ct
    )
    {
        var metadata = await ScalarLong(conn, "SELECT count(*) FROM trax.metadata", ct);
        var logs = await ScalarLong(conn, "SELECT count(*) FROM trax.log", ct);
        return metadata >= profile.Metadata * 0.95 && logs >= profile.Log * 0.95;
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
