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
            SELECT * FROM trax.work_queue w
            WHERE w.id = {0}
              AND w.status = 'queued'
              AND w.confirmed_at IS NOT NULL
              AND (
                w.subject_key IS NULL
                OR NOT EXISTS (
                    SELECT 1
                    FROM trax.work_queue b
                    JOIN trax.metadata m ON m.id = b.metadata_id
                    WHERE b.subject_key = w.subject_key
                      AND b.status = 'dispatched'
                      AND m.train_state IN ('pending', 'in_progress')
                )
              )
            FOR UPDATE SKIP LOCKED
            """;

    /// <inheritdoc />
    /// <remarks>
    /// The two-key form, with a fixed class key. Postgres keeps the two-key and single-key lock
    /// spaces apart, so a subject can never contend with the leader lock or with a consumer's
    /// own single-key advisory locks, whatever its hash.
    /// </remarks>
    public string LockSubject() =>
        "SELECT pg_advisory_xact_lock(hashtext('trax_subject'), hashtext({0}))";

    public string DequeueBackgroundJobs() =>
        """
            SELECT * FROM trax.background_job
            WHERE fetched_at IS NULL
               OR fetched_at < NOW() - make_interval(secs => {0})
            ORDER BY priority DESC, created_at ASC
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

    /// <remarks>
    /// Only confirmed entries are candidates, and a manual entry whose subject already has a run
    /// in flight is left out. The claim refuses both anyway, but a candidate the claim will refuse
    /// still takes a capacity slot in the cycle that loaded it, so letting them through here lets
    /// a backlog for one subject, or a few stranded staged entries, starve everything else.
    /// Manifest entries carry no subject, so only the manual branch needs the subject check.
    /// </remarks>
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
                  AND wq.confirmed_at IS NOT NULL
                  AND mg.is_enabled = true
                  AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW())
            )
            SELECT wq.* FROM trax.work_queue wq
            WHERE wq.id IN (SELECT ranked.id FROM ranked WHERE ranked.rn <= {0})
               OR (wq.manifest_id IS NULL AND wq.status = 'queued'
                   AND wq.confirmed_at IS NOT NULL
                   AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW())
                   AND (
                     wq.subject_key IS NULL
                     OR NOT EXISTS (
                         SELECT 1
                         FROM trax.work_queue b
                         JOIN trax.metadata bm ON bm.id = b.metadata_id
                         WHERE b.subject_key = wq.subject_key
                           AND b.status = 'dispatched'
                           AND bm.train_state IN ('pending', 'in_progress')
                     )
                   ))
            """;
}
