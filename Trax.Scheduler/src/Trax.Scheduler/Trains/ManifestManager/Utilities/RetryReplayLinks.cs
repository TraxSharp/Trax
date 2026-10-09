using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// The replay links a retry or a requeue reads and clears (docs/adr/0017), and the resume links
/// beside them (Trax.Docs/adr/0047).
/// </summary>
internal static class RetryReplayLinks
{
    /// <summary>
    /// The unique index that holds at most one queued entry per replayed run. Trax.Effect creates
    /// it (Postgres 062, Sqlite 027).
    /// </summary>
    internal const string QueuedReplayIndex = "ix_work_queue_unique_queued_replay";

    /// <summary>
    /// The unique index that holds at most one queued entry per resumed run. Trax.Effect creates
    /// it (Postgres 074, Sqlite 036). See Trax.Docs/adr/0047.
    /// </summary>
    internal const string QueuedResumeIndex = "ix_work_queue_unique_queued_resume";

    /// <summary>
    /// Whether <paramref name="exception"/>, or one it wraps, is an insert refused because a queued
    /// entry already replays the same run: a unique violation on <see cref="QueuedReplayIndex"/>,
    /// which Postgres names and Sqlite reports by its column. Any other failure, another unique
    /// index's included, is false.
    /// </summary>
    /// <param name="exception">The failure to read.</param>
    /// <param name="dialect">When known, it must also read the failure as a unique violation.</param>
    internal static bool IsQueuedReplayConflict(Exception exception, ISqlDialect? dialect = null) =>
        IsConflictOn(
            exception,
            dialect,
            QueuedReplayIndex,
            "UNIQUE constraint failed: work_queue.replay_decisions_of"
        );

    /// <summary>
    /// Whether <paramref name="exception"/>, or one it wraps, is an insert refused because a queued
    /// entry already resumes the same run: a unique violation on <see cref="QueuedResumeIndex"/>,
    /// which Postgres names and Sqlite reports by its column. Any other failure is false.
    /// </summary>
    /// <param name="exception">The failure to read.</param>
    /// <param name="dialect">When known, it must also read the failure as a unique violation.</param>
    internal static bool IsQueuedResumeConflict(Exception exception, ISqlDialect? dialect = null) =>
        IsConflictOn(
            exception,
            dialect,
            QueuedResumeIndex,
            "UNIQUE constraint failed: work_queue.resume_from"
        );

    private static bool IsConflictOn(
        Exception exception,
        ISqlDialect? dialect,
        string index,
        string sqliteMessage
    )
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is not DbUpdateException update)
                continue;
            if (dialect is not null && !dialect.IsUniqueViolation(update))
                return false;

            for (
                Exception? inner = update.InnerException;
                inner is not null;
                inner = inner.InnerException
            )
                if (
                    inner.Message.Contains(index, StringComparison.Ordinal)
                    || inner.Message.Contains(sqliteMessage, StringComparison.Ordinal)
                )
                    return true;
            return false;
        }
        return false;
    }

    /// <summary>
    /// Saves <paramref name="context"/>, and when the save is refused because a queued entry
    /// already replays the run one of <paramref name="entries"/> names, clears that entry's link
    /// and saves again, so it asks afresh rather than failing. On a second refusal every link
    /// among them is cleared. A refusal because a queued entry already resumes the run one of them
    /// names clears that entry's resume link the same way, so it reruns from the top
    /// (Trax.Docs/adr/0047). Any other failure propagates.
    /// </summary>
    /// <returns>How many replay links were cleared.</returns>
    internal static async Task<int> SaveAskingAfreshOnConflictAsync(
        IDataContext context,
        IReadOnlyCollection<WorkQueue> entries,
        ILogger logger,
        CancellationToken ct
    )
    {
        var cleared = 0;
        var replayConflicts = 0;
        var resumeConflicts = 0;

        while (true)
        {
            try
            {
                await context.SaveChanges(ct);
                return cleared;
            }
            catch (DbUpdateException ex)
                when (replayConflicts < 2
                    && IsQueuedReplayConflict(ex)
                    && entries.Any(e => e.ReplayDecisionsOf is not null)
                )
            {
                replayConflicts++;
                var linked = entries.Where(e => e.ReplayDecisionsOf is not null).ToList();
                var sources = linked.Select(e => e.ReplayDecisionsOf!.Value).ToList();
                var taken =
                    replayConflicts == 1
                        ? (
                            await context
                                .WorkQueues.AsNoTracking()
                                .Where(q =>
                                    q.ReplayDecisionsOf != null
                                    && sources.Contains(q.ReplayDecisionsOf.Value)
                                    && q.Status == WorkQueueStatus.Queued
                                )
                                .Select(q => q.ReplayDecisionsOf!.Value)
                                .ToListAsync(ct)
                        ).ToHashSet()
                        : sources.ToHashSet();

                foreach (var entry in linked.Where(e => taken.Contains(e.ReplayDecisionsOf!.Value)))
                {
                    logger.LogInformation(
                        "A queued entry already replays run {ReplayDecisionsOf}, so the entry "
                            + "queued for manifest {ManifestId} asks its deciders afresh",
                        entry.ReplayDecisionsOf,
                        entry.ManifestId
                    );
                    entry.ReplayDecisionsOf = null;
                    cleared++;
                }
            }
            catch (DbUpdateException ex)
                when (resumeConflicts < 2
                    && IsQueuedResumeConflict(ex)
                    && entries.Any(e => e.ResumeFrom is not null)
                )
            {
                resumeConflicts++;
                var linked = entries.Where(e => e.ResumeFrom is not null).ToList();
                var sources = linked.Select(e => e.ResumeFrom!.Value).ToList();
                var taken =
                    resumeConflicts == 1
                        ? (
                            await context
                                .WorkQueues.AsNoTracking()
                                .Where(q =>
                                    q.ResumeFrom != null
                                    && sources.Contains(q.ResumeFrom.Value)
                                    && q.Status == WorkQueueStatus.Queued
                                )
                                .Select(q => q.ResumeFrom!.Value)
                                .ToListAsync(ct)
                        ).ToHashSet()
                        : sources.ToHashSet();

                foreach (var entry in linked.Where(e => taken.Contains(e.ResumeFrom!.Value)))
                {
                    logger.LogInformation(
                        "A queued entry already resumes run {ResumeFrom}, so the entry queued for "
                            + "manifest {ManifestId} reruns from the top",
                        entry.ResumeFrom,
                        entry.ManifestId
                    );
                    entry.ResumeFrom = null;
                    entry.ResumeAt = null;
                }
            }
        }
    }

    /// <summary>
    /// Whether the answers of run <paramref name="metadataId"/> are already replayed: a run, in
    /// any state, that did not abandon its replay, or a still-queued entry names it in
    /// <c>replay_decisions_of</c>. A run's answers
    /// are replayed once, so a run for which this holds is not linked again.
    /// </summary>
    internal static async Task<bool> IsReplayedAsync(
        this IDataContext context,
        long metadataId,
        CancellationToken ct
    ) =>
        await context
            .Metadatas.AsNoTracking()
            .AnyAsync(m => m.ReplayDecisionsOf == metadataId && !m.ReplayAbandoned, ct)
        || await context
            .WorkQueues.AsNoTracking()
            .AnyAsync(
                q => q.ReplayDecisionsOf == metadataId && q.Status == WorkQueueStatus.Queued,
                ct
            );

    /// <summary>
    /// Clears <c>replay_decisions_of</c> on every still-queued entry of the given manifests.
    /// </summary>
    /// <param name="context">The context to clear through.</param>
    /// <param name="manifestIds">The manifests that no longer replay.</param>
    /// <param name="save">
    /// On a provider without set updates, whether to save the rows changed. False leaves them
    /// tracked for the caller's own save.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many entries were cleared.</returns>
    internal static async Task<int> ClearQueuedAsync(
        IDataContext context,
        IReadOnlyCollection<long> manifestIds,
        CancellationToken ct,
        bool save = true
    )
    {
        var linked = context.WorkQueues.Where(q =>
            q.ManifestId != null
            && manifestIds.Contains(q.ManifestId.Value)
            && q.Status == WorkQueueStatus.Queued
            && q.ReplayDecisionsOf != null
        );

        if (context.SupportsSetUpdates())
            return await linked.ExecuteUpdateAsync(
                s => s.SetProperty(q => q.ReplayDecisionsOf, (long?)null),
                ct
            );

        var rows = await linked.ToListAsync(ct);
        foreach (var row in rows)
            row.ReplayDecisionsOf = null;
        if (save && rows.Count > 0)
            await context.SaveChanges(ct);
        return rows.Count;
    }
}
