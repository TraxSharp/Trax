using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.Data.Postgres.Services.SqlDialect;

/// <summary>
/// PostgreSQL-specific SQL dialect using advisory locks, FOR UPDATE SKIP LOCKED,
/// and native time functions.
/// </summary>
internal class PostgresSqlDialect : ISqlDialect
{
    public FormattableString TryAcquireLeaderLock(string lockName) =>
        $"""SELECT pg_try_advisory_xact_lock(hashtext('{lockName}')) AS "Value" """;

    public string ClaimWorkQueueEntry() =>
        """
            SELECT * FROM trax.work_queue
            WHERE id = {0} AND status = 'queued'
            FOR UPDATE SKIP LOCKED
            """;

    public string DequeueBackgroundJobs() =>
        """
            SELECT * FROM trax.background_job
            WHERE fetched_at IS NULL
               OR fetched_at < NOW() - make_interval(secs => {0})
            ORDER BY priority DESC, created_at ASC
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

    public string LoadGroupFairQueuedJobs() =>
        """
            WITH ranked AS (
                SELECT wq.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.manifest_group_id
                           ORDER BY wq.priority DESC, wq.created_at ASC
                       ) AS rn
                FROM trax.work_queue wq
                JOIN trax.manifest m ON wq.manifest_id = m.id
                JOIN trax.manifest_group mg ON m.manifest_group_id = mg.id
                WHERE wq.status = 'queued'
                  AND mg.is_enabled = true
                  AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW())
            )
            SELECT wq.* FROM trax.work_queue wq
            WHERE wq.id IN (SELECT ranked.id FROM ranked WHERE ranked.rn <= {0})
               OR (wq.manifest_id IS NULL AND wq.status = 'queued'
                   AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW()))
            """;
}
