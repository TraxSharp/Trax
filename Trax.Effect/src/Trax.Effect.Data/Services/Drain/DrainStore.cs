using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using DrainRow = Trax.Effect.Models.Drain.Drain;

namespace Trax.Effect.Data.Services.Drain;

/// <summary>
/// Reads and writes the <c>trax.drain</c> row, and lists the work a drain waits on. Every caller goes through here:
/// the hosts that honour the drain, the operations service the console and the API share, and the process a deploy
/// starts with <c>--trax-drain</c>. Written in LINQ the three providers all run, compared on the caller's clock.
/// </summary>
internal static class DrainStore
{
    /// <summary>How many busy items a list carries at most; the total is counted apart.</summary>
    public const int BusyListCap = 50;

    /// <summary>The drain in force at <paramref name="now"/>, or null when there is none or it has lapsed.</summary>
    public static async Task<DrainRow?> ReadAsync(
        IDataContext context,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var row = await context
            .Drains.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == DrainRow.SingletonId, ct)
            .ConfigureAwait(false);
        return row is not null && DrainRow.Holds(row.Until, now) ? row : null;
    }

    /// <summary>Whether a drain is in force at <paramref name="now"/>.</summary>
    public static async Task<bool> IsDrainedAsync(
        IDataContext context,
        DateTimeOffset now,
        CancellationToken ct
    ) => await ReadAsync(context, now, ct).ConfigureAwait(false) is not null;

    /// <summary>
    /// Sets the drain to lapse at <paramref name="until"/>, replacing any drain already set, lapsed or not, and
    /// stamping it as started at <paramref name="now"/> by <paramref name="startedBy"/>.
    /// </summary>
    public static async Task SetAsync(
        IDataContext context,
        DateTimeOffset until,
        DateTimeOffset now,
        string startedBy,
        CancellationToken ct
    )
    {
        // Two callers setting it at once: the second insert hits the key and is retried as an update.
        for (var attempt = 0; ; attempt++)
        {
            var row = await context
                .Drains.FirstOrDefaultAsync(d => d.Id == DrainRow.SingletonId, ct)
                .ConfigureAwait(false);
            if (row is null)
            {
                row = new DrainRow();
                context.Drains.Add(row);
            }
            row.Until = until.UtcDateTime;
            row.StartedAt = now.UtcDateTime;
            row.StartedBy = startedBy;
            try
            {
                await context.SaveChanges(ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                context.Reset();
            }
        }
    }

    /// <summary>Ends the drain. Returns whether one was in force at <paramref name="now"/>; a lapsed row is deleted too.</summary>
    public static async Task<bool> ClearAsync(
        IDataContext context,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var row = await context
            .Drains.FirstOrDefaultAsync(d => d.Id == DrainRow.SingletonId, ct)
            .ConfigureAwait(false);
        if (row is null)
            return false;
        var held = DrainRow.Holds(row.Until, now);
        context.Drains.Remove(row);
        try
        {
            await context.SaveChanges(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another caller deleted it first; it is gone either way.
            context.Reset();
        }
        return held;
    }

    /// <summary>
    /// The work a drain waits on, at most <see cref="BusyListCap"/> items, runs first, with the total of each kind.
    /// </summary>
    /// <remarks>
    /// A run is busy while it is Pending or InProgress, unless it is one of <paramref name="ignoredTrains"/>, or it is
    /// Pending with its job still in the queue and claimed by no worker: that job waits in the database through the
    /// roll and the next hosts run it. A machine instance is busy while it is not at rest: an effect is in flight on
    /// it, or the run it invoked is busy.
    /// </remarks>
    public static async Task<(IReadOnlyList<DrainBusyItem> Items, int Total)> BusyAsync(
        IDataContext context,
        IReadOnlyCollection<string> ignoredTrains,
        CancellationToken ct
    )
    {
        var ignored = ignoredTrains.ToList();
        var liveRuns = context.Metadatas.Where(m =>
            (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            && !ignored.Contains(m.Name)
            && !(
                m.TrainState == TrainState.Pending
                && context.BackgroundJobs.Any(j => j.MetadataId == m.Id && j.FetchedAt == null)
            )
        );
        var busyInstances = context.SnapshotDrafts.Where(d =>
            d.EffectToken != null
            || (d.InvokeToken != null && liveRuns.Any(m => m.ExternalId == d.InvokeToken))
        );

        var runTotal = await liveRuns.CountAsync(ct).ConfigureAwait(false);
        var instanceTotal = await busyInstances.CountAsync(ct).ConfigureAwait(false);

        var items = await BusyRunsAsync(liveRuns, ct).ConfigureAwait(false);
        var room = BusyListCap - items.Count;
        if (room > 0 && instanceTotal > 0)
            items.AddRange(await BusyInstancesAsync(busyInstances, room, ct).ConfigureAwait(false));

        return (items, runTotal + instanceTotal);
    }

    private static async Task<List<DrainBusyItem>> BusyRunsAsync(
        IQueryable<Effect.Models.Metadata.Metadata> liveRuns,
        CancellationToken ct
    )
    {
        var runs = await liveRuns
            .OrderBy(m => m.Id)
            .Take(BusyListCap)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.HostName,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return runs.Select(r => new DrainBusyItem(
                DrainBusyItem.Run,
                r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.Name,
                r.TrainState.ToString(),
                DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc),
                string.IsNullOrEmpty(r.HostName) ? null : r.HostName
            ))
            .ToList();
    }

    private static async Task<IEnumerable<DrainBusyItem>> BusyInstancesAsync(
        IQueryable<Effect.Models.SnapshotDraft.SnapshotDraft> busyInstances,
        int take,
        CancellationToken ct
    )
    {
        var instances = await busyInstances
            .OrderBy(d => d.RowId)
            .Take(take)
            .Select(d => new
            {
                d.Machine,
                d.Id,
                d.State,
                d.EffectToken,
                d.EffectTrigger,
                d.UpdatedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return instances.Select(i => new DrainBusyItem(
            DrainBusyItem.Instance,
            $"{i.Machine}/{i.Id}",
            i.Machine,
            i.EffectToken is null
                ? $"{i.State}, waiting on a run"
                : $"{i.State}, effect {i.EffectTrigger} in flight",
            i.UpdatedAt.UtcDateTime,
            Host: null
        ));
    }
}
