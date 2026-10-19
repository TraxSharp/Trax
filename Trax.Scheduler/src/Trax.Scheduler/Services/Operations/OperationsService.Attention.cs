using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using HostHeartbeatRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Scheduler.Services.Operations;

// What needs attention, for the console's Overview (central docs/0022): six small reads, each
// through an index, run at once on contexts of their own, so the call costs the slowest of them.
public partial class OperationsService
{
    /// <summary>
    /// The most entries <see cref="GetAttentionAsync"/> returns in each of its lists: dead-letter
    /// groups, failing trains, stuck-instance groups and offline hosts. Well inside a page of an
    /// operations read (500).
    /// </summary>
    public const int AttentionListCap = 50;

    /// <inheritdoc />
    /// <remarks>
    /// <para>The reads: the live scheduler settings (no database); the dead letters awaiting
    /// intervention through <c>dead_letter_status_idx</c>, grouped by their manifest's train and
    /// their reason; the runs of the last seven days that completed or failed, through
    /// <c>ix_metadata_metrics_state_time</c> (Trax.Effect's Postgres migration 037), grouped by
    /// train and by whether they started in the last hour; the stuck instances, through the
    /// partial indexes on <c>due_at</c> and <c>invoke_token</c>; the queued entries, through the
    /// partial index on confirmed queued entries (<c>ix_work_queue_dispatchable</c>), counting those
    /// that are due; and <c>trax.host_heartbeat</c>, which holds one row per
    /// process seen in a day.</para>
    /// <para>A train is listed when it failed at least once in the last hour and its rate there is
    /// above its rate over the rest of the week, so a train that has always failed this often is
    /// not, and a train with no earlier finished run is listed for any failure.</para>
    /// <para>Each <c>Link</c> is a path in the operations console with the query its list page
    /// reads its filter from; enum values are written as the GraphQL schema spells them.</para>
    /// </remarks>
    public async Task<Attention> GetAttentionAsync(CancellationToken ct)
    {
        var clock = _services?.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = clock.GetUtcNow();
        var nowUtc = now.UtcDateTime;
        var hourAgo = nowUtc.AddHours(-1);
        var weekAgo = nowUtc.AddDays(-7);
        var overdueBefore = OverdueBefore();

        async Task<T> Read<T>(Func<IDataContext, Task<T>> read)
        {
            using var db = await _dataContextFactory.CreateDbContextAsync(ct);
            return await read(db);
        }

        var deadLettersRead = Read(db =>
            db.DeadLetters.AsNoTracking()
                .Where(d => d.Status == DeadLetterStatus.AwaitingIntervention)
                .GroupBy(d => new { d.Manifest!.Name, d.Reason })
                .Select(g => new
                {
                    g.Key.Name,
                    g.Key.Reason,
                    Count = g.LongCount(),
                    Oldest = g.Min(d => d.DeadLetteredAt),
                })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Name)
                .ThenBy(g => g.Reason)
                .Take(AttentionListCap)
                .ToListAsync(ct)
        );

        var finishedRead = Read(db =>
            db.Metadatas.AsNoTracking()
                .Where(m =>
                    (m.TrainState == TrainState.Completed || m.TrainState == TrainState.Failed)
                    && m.StartTime >= weekAgo
                )
                .GroupBy(m => new
                {
                    m.Name,
                    m.TrainState,
                    LastHour = m.StartTime >= hourAgo,
                })
                .Select(g => new
                {
                    g.Key.Name,
                    g.Key.TrainState,
                    g.Key.LastHour,
                    Count = g.LongCount(),
                })
                .ToListAsync(ct)
        );

        var stuckRead = Read(db => ReadStuckGroupsAsync(db, overdueBefore, ct));

        var queueRead = Read(async db =>
        {
            var due = db
                .WorkQueues.AsNoTracking()
                .Where(w =>
                    w.Status == WorkQueueStatus.Queued
                    && w.ConfirmedAt != null
                    && (w.ScheduledAt == null || w.ScheduledAt <= nowUtc)
                );
            var count = await due.LongCountAsync(ct);
            var oldest = count == 0 ? null : await due.MinAsync(w => (DateTime?)w.CreatedAt, ct);
            return (count, oldest);
        });

        var hostsRead = Read(db =>
            db.HostHeartbeats.AsNoTracking()
                .Select(h => new
                {
                    h.InstanceId,
                    h.HostName,
                    h.LastHeartbeatAt,
                    h.RunsDispatcher,
                })
                .ToListAsync(ct)
        );

        await Task.WhenAll(deadLettersRead, finishedRead, stuckRead, queueRead, hostsRead);

        var scheduler = new AttentionScheduler(
            _schedulerConfiguration.ManifestManagerEnabled,
            _schedulerConfiguration.JobDispatcherEnabled,
            "/system/scheduler"
        );

        var deadLetters = (await deadLettersRead)
            .Select(g => new AttentionDeadLetterGroup(
                g.Name,
                g.Reason,
                g.Count,
                DateTime.SpecifyKind(g.Oldest, DateTimeKind.Utc),
                ConsoleLink(
                    "/runs/dead-letters",
                    ("status", "AWAITING_INTERVENTION"),
                    ("train", g.Name),
                    ("reason", g.Reason)
                )
            ))
            .ToList();

        var failing = FailingTrains(
            (await finishedRead).Select(r => (r.Name, r.TrainState, r.LastHour, r.Count))
        );

        var stuck = (await stuckRead)
            .Select(g => new AttentionStuckInstances(
                g.Machine,
                g.State,
                g.Count,
                ConsoleLink(
                    $"/machines/{Uri.EscapeDataString(g.Machine)}",
                    ("state", g.State),
                    ("stuck", "true")
                )
            ))
            .ToList();

        var (queued, oldestQueued) = await queueRead;
        var oldestQueuedAt = oldestQueued is { } o
            ? DateTime.SpecifyKind(o, DateTimeKind.Utc)
            : (DateTime?)null;
        var queue = new AttentionQueue(
            queued,
            oldestQueuedAt,
            oldestQueuedAt is { } at ? Math.Max(0, (nowUtc - at).TotalSeconds) : null,
            ConsoleLink("/runs/queued", ("status", "QUEUED"))
        );

        var offline = (await hostsRead)
            .Where(h => !HostHeartbeatRow.IsOnline(h.LastHeartbeatAt, now))
            .OrderByDescending(h => h.LastHeartbeatAt)
            .ThenBy(h => h.InstanceId, StringComparer.Ordinal)
            .Take(AttentionListCap)
            .Select(h => new AttentionHost(
                h.InstanceId,
                h.HostName,
                DateTime.SpecifyKind(h.LastHeartbeatAt, DateTimeKind.Utc),
                h.RunsDispatcher,
                "/system/hosts"
            ))
            .ToList();

        return new Attention(scheduler, deadLetters, failing, stuck, queue, offline, nowUtc);

        static List<AttentionFailingTrain> FailingTrains(
            IEnumerable<(string Name, TrainState State, bool LastHour, long Count)> rows
        ) =>
            rows.GroupBy(r => r.Name, StringComparer.Ordinal)
                .Select(g =>
                {
                    long Of(bool lastHour, TrainState state) =>
                        g.Where(r => r.LastHour == lastHour && r.State == state).Sum(r => r.Count);
                    var failedHour = Of(true, TrainState.Failed);
                    var finishedHour = failedHour + Of(true, TrainState.Completed);
                    var failedBefore = Of(false, TrainState.Failed);
                    var finishedBefore = failedBefore + Of(false, TrainState.Completed);
                    return new AttentionFailingTrain(
                        g.Key,
                        failedHour,
                        finishedHour,
                        finishedHour == 0 ? 0 : (double)failedHour / finishedHour,
                        finishedBefore == 0 ? 0 : (double)failedBefore / finishedBefore,
                        ConsoleLink("/runs", ("state", "FAILED"), ("train", g.Key))
                    );
                })
                .Where(t => t.FailedLastHour > 0 && t.LastHourFailureRate > t.BaselineFailureRate)
                .OrderByDescending(t => t.LastHourFailureRate)
                .ThenByDescending(t => t.FailedLastHour)
                .ThenBy(t => t.TrainName, StringComparer.Ordinal)
                .Take(AttentionListCap)
                .ToList();
    }

    // The stuck instances by machine and state, by StuckPredicate: the overdue timers through ix_snapshot_draft_due,
    // the rows that hold a token through ux_snapshot_draft_invoke_token, and each token's run and queue entry through
    // their external-id indexes.
    private static async Task<
        List<(string Machine, string State, long Count)>
    > ReadStuckGroupsAsync(IDataContext db, DateTimeOffset overdueBefore, CancellationToken ct) => (
            await db
                .SnapshotDrafts.AsNoTracking()
                .Where(StuckPredicate(db, overdueBefore))
                .GroupBy(x => new { x.Machine, x.State })
                .Select(g => new
                {
                    g.Key.Machine,
                    g.Key.State,
                    Count = g.LongCount(),
                })
                .ToListAsync(ct)
        ).Select(g => (g.Machine, g.State, g.Count)).OrderByDescending(g => g.Count).ThenBy(g => g.Machine, StringComparer.Ordinal).ThenBy(g => g.State, StringComparer.Ordinal).Take(AttentionListCap).ToList();

    private static string ConsoleLink(string path, params (string Key, string Value)[] query) =>
        query.Length == 0
            ? path
            : path
                + "?"
                + string.Join(
                    "&",
                    query.Select(q =>
                        $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"
                    )
                );
}
