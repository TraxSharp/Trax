using System.Text.RegularExpressions;
using Npgsql;

namespace Trax.Dashboard.Tests.Stress.Fixtures;

/// <summary>
/// Row counts for a stress run. Override any value with the matching <c>TRAX_STRESS_*</c>
/// environment variable for a smaller or larger run.
/// </summary>
/// <param name="Metadata">Runs in the metadata table.</param>
/// <param name="WorkQueue">Entries in the work queue table.</param>
/// <param name="Manifests">Manifests, all in one group.</param>
/// <param name="BlobRows">
/// How many of the newest runs, entries and manifests carry a 1 MiB input, output or properties
/// value. These are the rows a grid's first page shows.
/// </param>
public sealed record GridStressProfile(long Metadata, long WorkQueue, int Manifests, int BlobRows)
{
    public static GridStressProfile FromEnvironment() =>
        new(
            Metadata: EnvLong("TRAX_STRESS_METADATA", 1_000_000),
            WorkQueue: EnvLong("TRAX_STRESS_WORKQUEUE", 500_000),
            Manifests: (int)EnvLong("TRAX_STRESS_MANIFEST", 5_000),
            BlobRows: (int)EnvLong("TRAX_STRESS_BLOB_ROWS", 200)
        );

    private static long EnvLong(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}

/// <summary>
/// Seeds the tables the dashboard's grids page through with server-side
/// <c>generate_series</c> inserts. The newest <see cref="GridStressProfile.BlobRows"/> rows of
/// each carry a 1 MiB value in the column the grid does not show, which is the Effect default cap
/// for a saved input. The value repeats a short string, so Postgres stores it compressed and the
/// seed stays small on disk, but a query that selects the column still decompresses and sends the
/// whole megabyte. Idempotent: a database already seeded to the profile is left as it is.
/// </summary>
public static class GridStressSeeder
{
    public const string TrainName = "Trax.Stress.Trains.IStressTrain";
    private const long ChunkSize = 250_000;
    private const int MinuteSpread = 20160;

    // A JSON object holding 1 MiB: 32,768 repeats of a 32-character md5. The columns are jsonb.
    private const string BlobSql =
        "('{\"blob\":\"' || repeat(md5(g::text), 32768) || '\"}')::jsonb";

    public static void EnsureDatabaseExists(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        if (!Regex.IsMatch(database, "^[a-z_][a-z0-9_]*$"))
            throw new ArgumentException(
                $"Database name '{database}' must be a snake_case ASCII identifier."
            );

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
            // Already exists.
        }
    }

    public static async Task SeedAsync(
        string connectionString,
        GridStressProfile profile,
        Action<string> log
    )
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        if (await AlreadySeededAsync(conn, profile))
        {
            log($"Already seeded (metadata≈{profile.Metadata:N0}); skipping.");
            return;
        }

        log("Truncating tables...");
        await ExecAsync(
            conn,
            "TRUNCATE trax.log, trax.work_queue, trax.dead_letter, trax.metadata, "
                + "trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await ExecAsync(conn, "INSERT INTO trax.manifest_group (name) VALUES ('stress-group')");

        log($"Seeding {profile.Manifests:N0} manifest...");
        await SeedTableAsync(
            conn,
            profile.Manifests,
            "INSERT INTO trax.manifest (external_id, name, manifest_group_id, schedule_type, properties) "
                + $"SELECT 'stress-manifest-' || g, '{TrainName}' || (g % 50), 1, 'none', "
                + $"       CASE WHEN g > {profile.Manifests - profile.BlobRows} THEN {BlobSql} END "
                + "FROM generate_series(@lo, @hi) g"
        );

        log($"Seeding {profile.Metadata:N0} metadata...");
        await SeedTableAsync(
            conn,
            profile.Metadata,
            "INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, manifest_id, input, output) "
                + "SELECT lpad(g::text, 32, '0'), "
                + $"       '{TrainName}' || (g % 50), "
                + "       (ARRAY['completed','completed','failed','in_progress','pending']::trax.train_state[])[1 + (g % 5)], "
                + $"       now() - (({profile.Metadata} - g) % {MinuteSpread}) * interval '1 minute', "
                + "       CASE WHEN (g % 5) < 3 THEN now() ELSE NULL END, "
                + $"       1 + (g % {profile.Manifests}), "
                + $"       CASE WHEN g > {profile.Metadata - profile.BlobRows} THEN {BlobSql} END, "
                + $"       CASE WHEN g > {profile.Metadata - profile.BlobRows} THEN {BlobSql} END "
                + "FROM generate_series(@lo, @hi) g"
        );

        // No queued entries: a queued entry for a manifest is unique, and the grid reads every
        // status the same way.
        log($"Seeding {profile.WorkQueue:N0} work_queue...");
        await SeedTableAsync(
            conn,
            profile.WorkQueue,
            "INSERT INTO trax.work_queue (external_id, train_name, status, created_at, priority, dispatch_attempts, input) "
                + "SELECT 'wq-' || g, "
                + $"       '{TrainName}' || (g % 50), "
                + "       (ARRAY['dispatched','cancelled']::trax.work_queue_status[])[1 + (g % 2)], "
                + "       now(), (g % 32), 0, "
                + $"       CASE WHEN g > {profile.WorkQueue - profile.BlobRows} THEN {BlobSql} END "
                + "FROM generate_series(@lo, @hi) g"
        );

        log($"Inserts done in {sw.Elapsed.TotalSeconds:F0}s. Running VACUUM ANALYZE...");
        await ExecAsync(
            conn,
            "VACUUM (ANALYZE, PARALLEL 0) trax.manifest_group, trax.manifest, trax.metadata, trax.work_queue"
        );
        log($"Seed complete in {sw.Elapsed.TotalSeconds:F0}s.");
    }

    private static async Task<bool> AlreadySeededAsync(
        NpgsqlConnection conn,
        GridStressProfile profile
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT (SELECT count(*) FROM trax.metadata), "
            + "(SELECT count(*) FROM trax.work_queue), "
            + "(SELECT count(*) FROM trax.metadata WHERE input IS NOT NULL)";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetInt64(0) == profile.Metadata
            && reader.GetInt64(1) == profile.WorkQueue
            && reader.GetInt64(2) == profile.BlobRows;
    }

    private static async Task SeedTableAsync(NpgsqlConnection conn, long total, string insertSql)
    {
        for (long lo = 1; lo <= total; lo += ChunkSize)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = insertSql;
            cmd.CommandTimeout = 1200;
            cmd.Parameters.AddWithValue("lo", lo);
            cmd.Parameters.AddWithValue("hi", Math.Min(lo + ChunkSize - 1, total));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 1200;
        await cmd.ExecuteNonQueryAsync();
    }
}
