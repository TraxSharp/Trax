using Microsoft.EntityFrameworkCore;

namespace Trax.Scheduler.Utilities;

/// <summary>
/// Reads the database's current UTC time, for a timestamp that is later compared with one written
/// by another process.
/// </summary>
/// <remarks>
/// A parent's success is stamped by whichever worker ran it and a dependent's dispatch by
/// whichever scheduler dispatched it. Stamped by their own clocks, a skew between the two
/// machines larger than the time between the parent finishing and the dependent starting made
/// every dependent run look as if it had started before the parent's latest success, so it was
/// queued again after every run. Both are stamped by the database instead, the one clock every
/// process shares.
/// <para>
/// The time is read through a query on a row the caller names, projected to
/// <see cref="DateTime.UtcNow"/>, which the providers translate to the server's clock
/// (<c>now()</c> on PostgreSQL, <c>strftime(..., 'now')</c> on SQLite). On PostgreSQL inside a
/// transaction that is the transaction's start. The in-memory provider has no server and
/// evaluates it in the process, which is the only clock it has.
/// </para>
/// </remarks>
internal static class DatabaseClock
{
    /// <summary>
    /// The database's current UTC time, read through <paramref name="row"/>. Falls back to this
    /// process's clock when the row is gone, since there is then nothing to compare it with.
    /// </summary>
    public static async Task<DateTime> UtcNowAsync<T>(
        IQueryable<T> row,
        CancellationToken cancellationToken
    )
    {
        var now = await row.Select(_ => (DateTime?)DateTime.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);

        return now is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : DateTime.UtcNow;
    }
}
