using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Dashboard.Tests.Integration.Fakes.Data;

/// <summary>
/// Registered so the operations service treats the in-memory store as one a scheduler
/// dispatches, as a host with a database provider is: on a host whose store is really in memory,
/// queueing is refused (Trax.Scheduler ADR 0019). Nothing here runs SQL, so every statement it
/// would build is refused.
/// </summary>
public sealed class StandInSqlDialect : ISqlDialect
{
    public FormattableString TryAcquireLeaderLock(string lockName) =>
        throw new NotSupportedException();

    public string ClaimWorkQueueEntry() => throw new NotSupportedException();

    public string DequeueBackgroundJobs() => throw new NotSupportedException();

    public string LoadGroupFairQueuedJobs() => throw new NotSupportedException();
}
