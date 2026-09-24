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

    /// <summary>
    /// Claims one confirmed, queued entry, unless its subject already has a run in flight.
    /// </summary>
    /// <remarks>
    /// The subject test is a set membership rather than a correlated <c>NOT EXISTS</c>. Correlated,
    /// it re-asked "does this subject have an in-flight run" by walking that subject's dispatched
    /// rows, and dispatched rows never reach a terminal status of their own: they are removed only
    /// by opt-in metadata cleanup, so they accumulate for the life of the subject. The cost grew
    /// with the subject's history rather than with the work in flight, and it grew inside the
    /// transaction holding the subject's advisory lock, so a busy subject blocked its own queue
    /// head. Measured at 130-170ms per claim against 500k dispatched rows on one subject, versus
    /// 0.34ms for the form below, which is bounded by the number of runs actually in flight.
    /// <para>
    /// <c>b.subject_key IS NOT NULL</c> is load-bearing, not tidiness: <c>x NOT IN (a, NULL)</c> is
    /// never true in SQL, so one dispatched run without a subject would otherwise refuse every
    /// keyed claim in the system. <c>SqliteDispatchSqlTests</c> pins it.
    /// </para>
    /// </remarks>
    public string ClaimWorkQueueEntry() =>
        """
            SELECT * FROM trax.work_queue w
            WHERE w.id = {0}
              AND w.status = 'queued'
              AND w.confirmed_at IS NOT NULL
              AND (
                w.subject_key IS NULL
                OR w.subject_key NOT IN (
                    SELECT b.subject_key
                    FROM trax.work_queue b
                    JOIN trax.metadata m ON m.id = b.metadata_id
                    WHERE b.status = 'dispatched'
                      AND b.subject_key IS NOT NULL
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
                     OR wq.subject_key NOT IN (
                         SELECT b.subject_key
                         FROM trax.work_queue b
                         JOIN trax.metadata bm ON bm.id = b.metadata_id
                         WHERE b.status = 'dispatched'
                           AND b.subject_key IS NOT NULL
                           AND bm.train_state IN ('pending', 'in_progress')
                     )
                   ))
            """;
}
