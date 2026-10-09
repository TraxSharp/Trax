using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// Fails a reaper's stale runs in one statement, and tells which of them that statement moved, so
/// a reaper publishes only the runs its own conditional write failed.
/// </summary>
/// <remarks>
/// The write is one <c>UPDATE ... WHERE</c> over every candidate, still guarded by the state the
/// reaper read them in, so a run that finished, or that another pass reaped, matches nothing. It
/// stamps each row it moves with an end time no other pass uses: a time later than any earlier
/// pass in this process took, to the microsecond a timestamp column keeps. The rows that carry
/// that end time and the reaper's failure are the rows this statement moved, read back in the
/// same transaction. Two processes would have to reap in the same microsecond to share one, and
/// on Postgres the leader lock lets only one process reap at a time.
/// </remarks>
internal static class ReapedRuns
{
    private static long _lastMark;

    /// <summary>
    /// Fails every run <paramref name="stillStale"/> matches, recording <paramref name="reason"/>,
    /// <paramref name="exception"/> and <paramref name="junction"/>, and returns the ids among
    /// <paramref name="candidates"/> this call moved.
    /// </summary>
    internal static async Task<List<long>> FailAsync(
        IDataContext dataContext,
        IQueryable<Metadata> stillStale,
        IReadOnlyCollection<long> candidates,
        string reason,
        string exception,
        string junction,
        CancellationToken cancellationToken
    )
    {
        var mark = NextMark();
        var moved = await stillStale.ExecuteUpdateAsync(
            s =>
                s.SetProperty(m => m.TrainState, TrainState.Failed)
                    .SetProperty(m => m.EndTime, mark)
                    .SetProperty(m => m.FailureReason, reason)
                    .SetProperty(m => m.FailureException, exception)
                    .SetProperty(m => m.FailureJunction, junction),
            cancellationToken
        );
        if (moved == 0)
            return [];

        var ids = candidates.ToList();
        return await dataContext
            .Metadatas.AsNoTracking()
            .Where(m =>
                ids.Contains(m.Id)
                && m.TrainState == TrainState.Failed
                && m.EndTime == mark
                && m.FailureException == exception
                && m.FailureJunction == junction
            )
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A UTC time to the microsecond, later than any this process has returned before.
    /// </summary>
    internal static DateTime NextMark()
    {
        var now = DateTime.UtcNow.Ticks;
        now -= now % TimeSpan.TicksPerMicrosecond;
        while (true)
        {
            var last = Interlocked.Read(ref _lastMark);
            var next = Math.Max(now, last + TimeSpan.TicksPerMicrosecond);
            if (Interlocked.CompareExchange(ref _lastMark, next, last) == last)
                return new DateTime(next, DateTimeKind.Utc);
        }
    }
}
