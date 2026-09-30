using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Services.SqlDialect;

/// <summary>
/// Provides provider-specific SQL for operations that differ across database backends.
/// </summary>
/// <remarks>
/// Postgres and SQLite have different SQL syntax for locking, time functions, and schema
/// namespacing. Each provider registers its own implementation. InMemory does not register
/// an implementation because its code path (gated by <c>HasDatabaseProvider = false</c>)
/// never executes raw SQL.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>
    /// Returns SQL that attempts to acquire a leader lock for single-instance coordination.
    /// The query must return a single boolean column aliased as <c>"Value"</c>.
    /// </summary>
    /// <param name="lockName">Logical name of the lock (e.g., "trax_manifest_manager").</param>
    FormattableString TryAcquireLeaderLock(string lockName);

    /// <summary>
    /// Returns SQL that selects a single work queue entry by ID with status 'queued',
    /// using provider-appropriate locking to prevent concurrent claims.
    /// Parameter <c>{0}</c> is the work queue entry ID.
    /// </summary>
    /// <remarks>
    /// The claim refuses an entry whose manifest is disabled unless the entry is an explicit
    /// trigger (<c>WorkQueue.IsExplicitTrigger</c>), the same rule
    /// <see cref="LoadGroupFairQueuedJobs"/> applies. The load only chooses candidates; the claim
    /// is the last read before dispatch, so a manifest disabled after its entry was loaded is
    /// still held.
    /// </remarks>
    string ClaimWorkQueueEntry();

    /// <summary>
    /// Returns SQL that blocks until no other transaction holds this subject, so two entries
    /// naming the same subject cannot be claimed at the same moment. Parameter <c>{0}</c> is the
    /// subject key. The lock is held for the claim transaction and released when it commits.
    /// </summary>
    /// <remarks>
    /// Row locking is not enough on its own: two entries for one subject are two different rows,
    /// so <c>FOR UPDATE SKIP LOCKED</c> does not make them contend, and while both are still
    /// queued neither can see a dispatched sibling to refuse itself.
    ///
    /// Defaults to a no-op, which is correct for providers with a single writer and keeps this
    /// from breaking implementations outside this repo. It still references the parameter, because
    /// a command carrying an unreferenced parameter is rejected by some providers.
    /// </remarks>
    string LockSubject() => "SELECT {0}";

    /// <summary>
    /// Returns SQL that selects background jobs eligible for dequeue, using
    /// provider-appropriate locking and time functions.
    /// Parameter <c>{0}</c> is visibility timeout in seconds, <c>{1}</c> is batch size.
    /// </summary>
    string DequeueBackgroundJobs();

    /// <summary>
    /// Returns SQL that reads the database's own estimate of how many rows a Trax table holds,
    /// or <c>null</c> when this provider keeps no such estimate. Parameter <c>{0}</c> is the
    /// table's unqualified name (for example <c>log</c>). The query returns one <c>bigint</c>
    /// column aliased as <c>"Value"</c>, and no row at all when there is no estimate to give:
    /// the table does not exist, or it has never been analyzed.
    /// </summary>
    /// <remarks>
    /// An estimate is for a count shown to a person, such as a page total over millions of log
    /// rows, where an exact <c>COUNT(*)</c> would scan the whole table. It is as stale as the
    /// table's last analyze, so never branch on it. When this returns <c>null</c>, or the query
    /// returns no row, count exactly instead.
    ///
    /// Defaults to <c>null</c>, which is always a correct answer and keeps this from breaking
    /// implementations outside this repo.
    /// </remarks>
    string? EstimateRowCount() => null;

    /// <summary>
    /// Whether a failed save was refused because a primary key or unique index already holds the
    /// value it wrote: Postgres <c>23505</c>, Sqlite <c>SQLITE_CONSTRAINT</c> with the
    /// <c>PRIMARYKEY</c> or <c>UNIQUE</c> extended code. Every other failure, another constraint
    /// violation included, is false.
    /// </summary>
    /// <remarks>
    /// This is how a caller reads "someone else got there first" from an insert without writing
    /// provider SQL, and without mistaking an unrelated database error for that answer: catch
    /// <see cref="DbUpdateException"/> only <c>when</c> this is true, and let anything else throw.
    ///
    /// Defaults to <c>false</c>, which keeps this from breaking implementations outside this repo
    /// and fails the safe way: a conflict such a provider does not recognise throws instead of
    /// being read as one.
    /// </remarks>
    bool IsUniqueViolation(DbUpdateException exception) => false;

    /// <summary>
    /// Whether <paramref name="exception"/> is a failure that may succeed if the same work is
    /// tried again: a lost or refused connection, a timeout, a deadlock or serialization failure,
    /// a server not yet accepting connections, or (Sqlite) a busy or locked database.
    /// The exception and every exception it wraps are examined, so a
    /// <see cref="DbUpdateException"/>, or EF's <see cref="InvalidOperationException"/> around a
    /// retried failure, is classified by the database error inside it.
    /// </summary>
    /// <remarks>
    /// A caller retrying work against the database asks this rather than matching the provider's
    /// exception types by name, which would treat every database error as transient, a constraint
    /// violation or a syntax error included.
    ///
    /// Defaults to <c>false</c>, which keeps this from breaking implementations outside this repo
    /// and fails the safe way: a provider that does not recognise a failure reports it rather than
    /// retrying it.
    /// </remarks>
    bool IsTransient(Exception exception) => false;

    /// <summary>
    /// Returns SQL that loads queued work queue entries with group-fair batching
    /// using a CTE with window functions. Parameter <c>{0}</c> is the per-group limit.
    /// </summary>
    /// <remarks>
    /// An entry whose manifest group is disabled is never loaded. An entry whose manifest is
    /// disabled is loaded only when it is an explicit trigger (<c>WorkQueue.IsExplicitTrigger</c>),
    /// so a paused manifest's scheduled entries take no slot in the per-group limit.
    /// </remarks>
    string LoadGroupFairQueuedJobs();
}
