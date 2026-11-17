using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Models.WorkQueue;

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
/// The time is read through a query on a row the caller names, projected to a read of
/// <c>DateTime.UtcNow</c>, which the providers translate to the server's clock (<c>now()</c> on
/// PostgreSQL, <c>strftime(..., 'now')</c> on SQLite). On PostgreSQL inside a transaction that is
/// the transaction's start. The in-memory provider has no server and evaluates it in the
/// process, which is the only clock it has.
/// </para>
/// <para>
/// The read is built as an expression node from the property, not written as code: written, it
/// would read like this process's clock, which the scheduler otherwise takes from a
/// <see cref="TimeProvider"/>. Only the query provider evaluates it.
/// </para>
/// </remarks>
internal static class DatabaseClock
{
    private static readonly Expression ServerUtcNow = Expression.Property(
        expression: null,
        typeof(DateTime).GetProperty("UtcNow").NotNull()
    );

    /// <summary>
    /// The database's current UTC time, read through <paramref name="row"/>. Falls back to
    /// <paramref name="timeProvider"/> when the row is gone, since there is then nothing to
    /// compare it with.
    /// </summary>
    public static async Task<DateTime> UtcNowAsync<T>(
        IQueryable<T> row,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var now = await row.Select(ServerNow<T>())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return now is { } value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// A projection of any row to the database's current time, which the provider translates to
    /// the server's clock.
    /// </summary>
    public static Expression<Func<T, DateTime?>> ServerNow<T>() =>
        Expression.Lambda<Func<T, DateTime?>>(
            Expression.Convert(ServerUtcNow, typeof(DateTime?)),
            Expression.Parameter(typeof(T), "_")
        );

    /// <summary>
    /// Whether a work queue entry is due by the database's clock: it has no scheduled time, or
    /// that time has come.
    /// </summary>
    public static Expression<Func<WorkQueue, bool>> IsDue()
    {
        var entry = Expression.Parameter(typeof(WorkQueue), "q");
        var scheduledAt = Expression.Property(entry, nameof(WorkQueue.ScheduledAt));
        return Expression.Lambda<Func<WorkQueue, bool>>(
            Expression.OrElse(
                Expression.Equal(scheduledAt, Expression.Constant(null, typeof(DateTime?))),
                Expression.LessThanOrEqual(
                    scheduledAt,
                    Expression.Convert(ServerUtcNow, typeof(DateTime?))
                )
            ),
            entry
        );
    }
}
