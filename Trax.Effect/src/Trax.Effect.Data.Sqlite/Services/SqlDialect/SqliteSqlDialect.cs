using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Sqlite.Services.SqlDialect;

/// <summary>
/// SQLite-specific SQL dialect. Uses datetime() functions, no schema prefix, and no
/// FOR UPDATE SKIP LOCKED (SQLite serializes writes via BEGIN IMMEDIATE).
/// </summary>
/// <remarks>
/// The SQLite context maps enums with EF's default, which stores the underlying integer, so an
/// enum column holds <c>0</c>, not <c>'queued'</c>. The literals below are generated from the
/// enums for that reason: a label compared against those columns matches nothing, which once
/// meant the claim never found an entry.
/// </remarks>
internal class SqliteSqlDialect : ISqlDialect
{
    private const int Queued = (int)WorkQueueStatus.Queued;
    private const int Dispatched = (int)WorkQueueStatus.Dispatched;
    private const int Pending = (int)TrainState.Pending;
    private const int InProgress = (int)TrainState.InProgress;

    /// <summary>
    /// SQLite is single-process, so the leader lock always succeeds.
    /// Multi-server coordination is not supported with SQLite.
    /// </summary>
    public FormattableString TryAcquireLeaderLock(string lockName) => $"""SELECT 1 AS "Value" """;

    /// <summary>
    /// <c>SQLITE_CONSTRAINT</c> with the <c>PRIMARYKEY</c> or <c>UNIQUE</c> extended code. The
    /// primary code alone is not enough: a <c>CHECK</c>, <c>NOT NULL</c> or foreign key failure
    /// shares it.
    /// </summary>
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException
            is SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_PRIMARYKEY
                    or SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE,
            };

    /// <summary>
    /// <c>SQLITE_BUSY</c> or <c>SQLITE_LOCKED</c>, which another connection's write causes and
    /// which clears when it commits, or a <see cref="TimeoutException"/>, anywhere in the exception
    /// or what it wraps. The primary code is compared, so the extended busy and locked codes count
    /// too.
    /// </summary>
    public bool IsTransient(Exception exception) =>
        AnyInChain(
            exception,
            e =>
                e
                    is TimeoutException
                        or SqliteException
                        {
                            SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY
                                or SQLitePCL.raw.SQLITE_LOCKED,
                        }
        );

    public string ClaimWorkQueueEntry() =>
        $$"""
            SELECT * FROM work_queue w
            WHERE w.id = {0}
              AND w.status = {{Queued}}
              AND w.confirmed_at IS NOT NULL
              AND (
                w.manifest_id IS NULL
                OR w.is_explicit_trigger = 1
                OR EXISTS (
                    SELECT 1 FROM manifest wm
                    WHERE wm.id = w.manifest_id AND wm.is_enabled = 1
                )
              )
              AND (
                w.subject_key IS NULL
                OR w.subject_key NOT IN (
                    SELECT b.subject_key
                    FROM work_queue b
                    JOIN metadata m ON m.id = b.metadata_id
                    WHERE b.status = {{Dispatched}}
                      AND b.subject_key IS NOT NULL
                      AND m.train_state IN ({{Pending}}, {{InProgress}})
                )
              )
            """;

    /// <summary>
    /// SQLite keeps no row estimate a query can read (<c>sqlite_stat1</c> exists only after an
    /// <c>ANALYZE</c> nothing here runs), and a SQLite database is small enough to count, so a
    /// caller counts exactly.
    /// </summary>
    public string? EstimateRowCount() => null;

    public string DequeueBackgroundJobs() =>
        """
            SELECT * FROM background_job
            WHERE fetched_at IS NULL
               OR fetched_at < datetime('now', '-' || CAST({0} AS TEXT) || ' seconds')
            ORDER BY priority DESC, created_at ASC
            LIMIT {1}
            """;

    /// <remarks>
    /// Only confirmed entries are candidates, and a manual entry whose subject already has a run
    /// in flight is left out. The claim refuses both anyway, but a candidate the claim will refuse
    /// still takes a capacity slot in the cycle that loaded it, so letting them through here lets
    /// a backlog for one subject, or a few stranded staged entries, starve everything else.
    /// Manifest entries carry no subject, so only the manual branch needs the subject check.
    /// </remarks>
    public string LoadGroupFairQueuedJobs() =>
        $$"""
            WITH ranked AS (
                SELECT wq.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.manifest_group_id
                           ORDER BY wq.priority DESC, wq.created_at ASC
                       ) AS rn
                FROM work_queue wq
                JOIN manifest m ON wq.manifest_id = m.id
                JOIN manifest_group mg ON m.manifest_group_id = mg.id
                WHERE wq.status = {{Queued}}
                  AND wq.confirmed_at IS NOT NULL
                  AND mg.is_enabled = 1
                  AND (m.is_enabled = 1 OR wq.is_explicit_trigger = 1)
                  AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= datetime('now'))
            )
            SELECT wq.* FROM work_queue wq
            WHERE wq.id IN (SELECT ranked.id FROM ranked WHERE ranked.rn <= {0})
               OR (wq.manifest_id IS NULL AND wq.status = {{Queued}}
                   AND wq.confirmed_at IS NOT NULL
                   AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= datetime('now'))
                   AND (
                     wq.subject_key IS NULL
                     OR wq.subject_key NOT IN (
                         SELECT b.subject_key
                         FROM work_queue b
                         JOIN metadata bm ON bm.id = b.metadata_id
                         WHERE b.status = {{Dispatched}}
                           AND b.subject_key IS NOT NULL
                           AND bm.train_state IN ({{Pending}}, {{InProgress}})
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
