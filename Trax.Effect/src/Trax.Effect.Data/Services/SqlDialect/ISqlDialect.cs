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
    /// Returns SQL that loads queued work queue entries with group-fair batching
    /// using a CTE with window functions. Parameter <c>{0}</c> is the per-group limit.
    /// </summary>
    string LoadGroupFairQueuedJobs();
}
