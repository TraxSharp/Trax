using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.Data.Sqlite.Services.SqlDialect;

/// <summary>
/// SQLite-specific SQL dialect. Uses datetime() functions, no schema prefix, and no
/// FOR UPDATE SKIP LOCKED (SQLite serializes writes via BEGIN IMMEDIATE).
/// </summary>
internal class SqliteSqlDialect : ISqlDialect
{
    /// <summary>
    /// SQLite is single-process, so the leader lock always succeeds.
    /// Multi-server coordination is not supported with SQLite.
    /// </summary>
    public FormattableString TryAcquireLeaderLock(string lockName) => $"""SELECT 1 AS "Value" """;

    public string ClaimWorkQueueEntry() =>
        """
            SELECT * FROM work_queue
            WHERE id = {0} AND status = 'queued'
            """;

    public string DequeueBackgroundJobs() =>
        """
            SELECT * FROM background_job
            WHERE fetched_at IS NULL
               OR fetched_at < datetime('now', '-' || CAST({0} AS TEXT) || ' seconds')
            ORDER BY priority DESC, created_at ASC
            LIMIT {1}
            """;

    public string LoadGroupFairQueuedJobs() =>
        """
            WITH ranked AS (
                SELECT wq.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.manifest_group_id
                           ORDER BY wq.priority DESC, wq.created_at ASC
                       ) AS rn
                FROM work_queue wq
                JOIN manifest m ON wq.manifest_id = m.id
                JOIN manifest_group mg ON m.manifest_group_id = mg.id
                WHERE wq.status = 'queued'
                  AND mg.is_enabled = 1
                  AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= datetime('now'))
            )
            SELECT wq.* FROM work_queue wq
            WHERE wq.id IN (SELECT ranked.id FROM ranked WHERE ranked.rn <= {0})
               OR (wq.manifest_id IS NULL AND wq.status = 'queued'
                   AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= datetime('now')))
            """;
}
