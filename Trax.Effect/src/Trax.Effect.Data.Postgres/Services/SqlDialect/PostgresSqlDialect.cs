using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.Data.Postgres.Services.SqlDialect;

/// <summary>
/// PostgreSQL-specific SQL dialect using advisory locks, FOR UPDATE SKIP LOCKED,
/// and native time functions.
/// </summary>
internal class PostgresSqlDialect : ISqlDialect
{
    /// <remarks>
    /// The name is a parameter, so it must not sit inside SQL quotes: quoted, the placeholder EF
    /// substitutes is hashed as literal text, and every name takes the same lock.
    /// </remarks>
    public FormattableString TryAcquireLeaderLock(string lockName) =>
        $"""SELECT pg_try_advisory_xact_lock(hashtext({lockName})) AS "Value" """;

    /// <summary>
    /// A <see cref="NpgsqlException"/> that Npgsql itself reports as transient (a connection that
    /// broke or could not be opened, a timeout, and the server errors it lists: serialization
    /// failure, deadlock, too many connections, a server not yet accepting connections, and the
    /// like), or a <see cref="TimeoutException"/>, anywhere in the exception or what it wraps.
    /// </summary>
    /// <remarks>
    /// Npgsql's own list is used rather than one kept here, so it follows the driver. Every other
    /// Postgres error is permanent to this: a constraint violation, a missing table or a syntax
    /// error fails the same way on every try.
    /// </remarks>
    public bool IsTransient(Exception exception) =>
        AnyInChain(exception, e => e is TimeoutException or NpgsqlException { IsTransient: true });

    /// <summary>SQLSTATE <c>23505</c>, <c>unique_violation</c>, which a primary key raises too.</summary>
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException
            is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Reads <c>pg_class.reltuples</c> for a table in the <c>trax</c> schema.
    /// </summary>
    /// <remarks>
    /// Since Postgres 14, <c>reltuples</c> is <c>-1</c> for a table that has never been analyzed
    /// or vacuumed. That means "unknown", not a count, so it is filtered out and the caller sees
    /// no row, the same as for a table that does not exist.
    /// </remarks>
    public string EstimateRowCount() =>
        """
            SELECT c.reltuples::bigint AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'trax'
              AND c.relname = {0}
              AND c.relkind IN ('r', 'p')
              AND c.reltuples >= 0
            """;

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
    /// The manifest test is an <c>EXISTS</c> rather than a join because <c>FOR UPDATE</c> locks a
    /// row of every table in the <c>FROM</c> list: joined, each claim would lock the manifest row,
    /// and claims for one manifest would contend with each other and with every manifest update.
    /// </para>
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
                w.manifest_id IS NULL
                OR w.is_explicit_trigger
                OR EXISTS (
                    SELECT 1 FROM trax.manifest wm
                    WHERE wm.id = w.manifest_id AND wm.is_enabled
                )
              )
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
                  AND (m.is_enabled OR wq.is_explicit_trigger)
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

    /// <summary>
    /// Whether <paramref name="exception"/> or anything it wraps matches. Every inner exception of
    /// an <see cref="AggregateException"/> is followed, and the walk is bounded so a cycle ends it.
    /// </summary>
    private static bool AnyInChain(Exception exception, Func<Exception, bool> match)
    {
        var pending = new Stack<Exception>([exception]);
        for (var seen = 0; pending.Count > 0 && seen < 64; seen++)
        {
            var current = pending.Pop();
            if (match(current))
                return true;

            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            else if (current.InnerException is { } inner)
                pending.Push(inner);
        }

        return false;
    }
}
