using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Trains.MetadataCleanup.Junctions;

/// <summary>
/// Deletes expired metadata and associated work queue entries and log entries for whitelisted train types.
/// </summary>
/// <remarks>
/// Deletes in configurable batches (default: 1000 rows) to limit row-level lock duration.
/// Each batch loads metadata IDs first, then clears back-references, deletes owned FK rows,
/// and deletes the metadata by ID. The junction loops until no more expired rows remain.
///
/// Internal scheduler trains (JobDispatcher, ManifestManager, MetadataCleanup, DeadLetterCleanup,
/// JobRunner) are always eligible regardless of the configured whitelist. The dispatcher alone
/// persists a metadata row every poll, so leaving these out lets the table grow without bound.
///
/// Trains added with a retention of their own are swept at that cutoff instead of the configured
/// default. Trains sharing a cutoff are swept together, so the batching below runs once per
/// distinct retention rather than once in total.
///
/// Only metadata in a terminal state (Completed, Failed, or Cancelled) is eligible for deletion,
/// and not while a queued work queue entry or a run that is kept names it in
/// <c>replay_decisions_of</c> (central <c>docs/0041</c>). A run that replays another is deleted only
/// in the same transaction as the run it replays, or once that run is gone, so no run is ever
/// kept while a replay of it has been deleted (docs/adr/0017).
/// <c>resume_from</c> is kept together the same way, since a run's checkpoints go with it: a run
/// is kept while a queued entry or a run that stays resumes it, and a resumed run is deleted with
/// the run it resumes, or once that run is gone. An entry no longer queued that names a deleted run
/// has its resume link cleared, so no row names a run that is gone (Trax.Docs/adr/0047).
/// A run a state machine invoked is kept while an instance still holds its invoke token, so its
/// outcome is still delivered from it (central <c>docs/0046</c>).
/// A batch that fails (for example an unexpected foreign-key reference) is bisected to isolate the
/// offending row, which is logged and skipped so one bad row can never abort the whole sweep.
/// </remarks>
internal class DeleteExpiredMetadataJunction(
    IDataContext dataContext,
    SchedulerConfiguration configuration,
    ILogger<DeleteExpiredMetadataJunction> logger,
    ITrainDiscoveryService? discoveryService = null
) : EffectJunction<MetadataCleanupRequest, Unit>
{
    public override async Task<Unit> Run(MetadataCleanupRequest input)
    {
        var cleanupConfig = configuration.MetadataCleanup!;
        var plan = MetadataRetentionPlan.Build(cleanupConfig, discoveryService, out var conflicts);

        // A conflict is refused at startup by MetadataCleanupConfigurationValidator, so reaching
        // one here means the validator did not run (it is registered alongside the polling
        // service). Warn and carry on with the longest retention rather than throwing: a cleanup
        // train that fails every cycle stops pruning altogether, which is the worse outcome.
        foreach (var conflict in conflicts)
            logger.LogWarning(
                "Conflicting metadata retention, keeping the longer of the two. {Conflict}",
                conflict.ToString()
            );

        var now = DateTime.UtcNow;
        var batchSize = BatchSize(cleanupConfig.DeleteBatchSize);
        var totals = new CleanupTotals();

        // Rows that could not be deleted are excluded from later batches so the sweep makes
        // progress instead of re-selecting the same poison rows forever. Shared across groups,
        // because a poison row is poison whichever cutoff selected it. Rows kept because of a
        // replay link join them for the rest of this sweep, for the same reason, and so does a
        // batch left after it kept being linked while it was deleted.
        var skippedIds = new List<long>();

        foreach (var (retention, names) in MetadataRetentionPlan.GroupByRetention(plan))
        {
            var cutoffTime = TimeCutoff.Before(now, retention);

            logger.LogDebug(
                "Deleting metadata older than {CutoffTime} (retention {Retention}) for train types [{Whitelist}]",
                cutoffTime,
                retention,
                string.Join(", ", names)
            );

            var eligible = dataContext
                .Metadatas.Where(m => names.Contains(m.Name))
                .Where(m => m.StartTime < cutoffTime)
                .Where(m =>
                    m.TrainState == TrainState.Completed
                    || m.TrainState == TrainState.Failed
                    || m.TrainState == TrainState.Cancelled
                )
                .Where(m => !skippedIds.Contains(m.Id))
                // A run a state machine invoked is kept while an instance still holds its token: its outcome
                // has not been delivered, and the delivery reads how it ended from this row. A token is never
                // set again once cleared (each entry mints a new run), so a run that passes this test cannot
                // become held again before it is deleted, and the selection needs no recheck for it.
                .Where(m =>
                    m.InvokingMachine == null
                    || !dataContext.SnapshotDrafts.Any(d => d.InvokeToken == m.ExternalId)
                );

            while (true)
            {
                // A run another run will replay is kept while anything that stays points at it: a
                // queued requeue that has not been dispatched, or a run that replayed it and may
                // itself be requeued, whose replay follows the link back. Deleting it takes its
                // decisions with it, and the replay then fails rather than asking afresh. A run
                // that replays another is kept while the run it replays is: deleted alone, it
                // would leave that run looking as though nothing had replayed it, and a retry
                // would replay the same answers again. So a replay and the run it replays, both
                // expired, are selected and deleted together.
                var query = eligible
                    .Where(m =>
                        !dataContext.WorkQueues.Any(q =>
                            q.ReplayDecisionsOf == m.Id && q.Status == WorkQueueStatus.Queued
                        )
                    )
                    .Where(m =>
                        !dataContext.Metadatas.Any(r =>
                            r.ReplayDecisionsOf == m.Id && !eligible.Any(e => e.Id == r.Id)
                        )
                    )
                    .Where(m =>
                        m.ReplayDecisionsOf == null
                        || !dataContext.Metadatas.Any(s => s.Id == m.ReplayDecisionsOf)
                        || eligible.Any(e => e.Id == m.ReplayDecisionsOf)
                    )
                    // The same for a resume (Trax.Docs/adr/0047): a run a queued entry or a run
                    // that stays resumes is kept, since its checkpoints go with it, and a resumed
                    // run goes with the run it resumed.
                    .Where(m =>
                        !dataContext.WorkQueues.Any(q =>
                            q.ResumeFrom == m.Id && q.Status == WorkQueueStatus.Queued
                        )
                    )
                    .Where(m =>
                        !dataContext.Metadatas.Any(r =>
                            r.ResumeFrom == m.Id && !eligible.Any(e => e.Id == r.Id)
                        )
                    )
                    .Where(m =>
                        m.ResumeFrom == null
                        || !dataContext.Metadatas.Any(s => s.Id == m.ResumeFrom)
                        || eligible.Any(e => e.Id == m.ResumeFrom)
                    )
                    .OrderBy(m => m.Id)
                    .Select(m => m.Id);

                var selected = await query.Take(batchSize).ToListAsync(CancellationToken);

                if (selected.Count == 0)
                    break;

                var batchIds = await WithReplayRelativesAsync(selected, eligible, skippedIds);

                if (batchIds.Count > 0)
                    await DeleteBatch(batchIds, totals, skippedIds);

                if (selected.Count < batchSize)
                    break;
            }
        }

        if (totals.Metadata > 0 || totals.Skipped > 0)
        {
            logger.LogInformation(
                "Metadata cleanup completed: deleted {MetadataCount} metadata, {WorkQueueCount} work queue entries, {LogCount} log entries; skipped {SkippedCount} undeletable rows",
                totals.Metadata,
                totals.WorkQueues,
                totals.Logs,
                totals.Skipped
            );
        }
        else
        {
            logger.LogDebug("Metadata cleanup completed: no expired entries found");
        }

        return Unit.Default;
    }

    /// <summary>
    /// The batch a sweep deletes at a time. Never unbounded: a batch is one transaction holding
    /// every log, entry and decision its runs own (see MetadataCleanupConfiguration), so null, and
    /// a value the startup validation did not see, are capped.
    /// </summary>
    internal static int BatchSize(int? configured) =>
        Math.Clamp(
            configured ?? SchedulerConfigLimits.MaxDeleteBatchSize,
            1,
            SchedulerConfigLimits.MaxDeleteBatchSize
        );

    /// <summary>
    /// Adds to <paramref name="selected"/> the eligible runs it must be deleted together with (the
    /// runs its runs replay, and the runs replaying them), then drops every run that would leave a
    /// kept run linked to a deleted one. A dropped run is added to <paramref name="excluded"/> so
    /// the sweep does not select it again.
    /// </summary>
    private async Task<List<long>> WithReplayRelativesAsync(
        List<long> selected,
        IQueryable<Effect.Models.Metadata.Metadata> eligible,
        List<long> excluded
    )
    {
        var batch = selected.ToHashSet();

        // Replay chains are short (a requeue of a requeue), so a few rounds close them.
        for (var round = 0; round < MaxReplayChainRounds; round++)
        {
            var ids = batch.ToList();
            var relatives = await eligible
                .Where(m =>
                    !ids.Contains(m.Id)
                    && (
                        (m.ReplayDecisionsOf != null && ids.Contains(m.ReplayDecisionsOf.Value))
                        || dataContext.Metadatas.Any(r =>
                            ids.Contains(r.Id) && r.ReplayDecisionsOf == m.Id
                        )
                        || (m.ResumeFrom != null && ids.Contains(m.ResumeFrom.Value))
                        || dataContext.Metadatas.Any(r =>
                            ids.Contains(r.Id) && r.ResumeFrom == m.Id
                        )
                    )
                )
                .Select(m => m.Id)
                .ToListAsync(CancellationToken);

            if (relatives.Count == 0)
                break;
            batch.UnionWith(relatives);
        }

        var kept = await KeptAsync(dataContext, batch.ToList(), CancellationToken);
        excluded.AddRange(kept);
        batch.ExceptWith(kept);
        return batch.OrderBy(id => id).ToList();
    }

    /// <summary>
    /// How many rounds <see cref="WithReplayRelativesAsync"/> follows replay links. A chain longer
    /// than this is left with its unreached end kept, and finished by a later sweep.
    /// </summary>
    private const int MaxReplayChainRounds = 8;

    /// <summary>
    /// Deletes a batch of metadata rows. On failure the batch is bisected to isolate the offending
    /// row; a single row that still fails is logged and added to <paramref name="skippedIds"/> so
    /// the sweep continues rather than aborting.
    /// </summary>
    private async Task DeleteBatch(
        IReadOnlyList<long> batchIds,
        CleanupTotals totals,
        List<long> skippedIds
    )
    {
        try
        {
            await DeleteMetadataByIds(batchIds, totals, skippedIds);
        }
        catch (Exception ex) when (batchIds.Count > 1)
        {
            logger.LogWarning(
                ex,
                "Metadata cleanup batch of {Count} failed; bisecting to isolate the bad row(s)",
                batchIds.Count
            );

            var mid = batchIds.Count / 2;
            await DeleteBatch(batchIds.Take(mid).ToList(), totals, skippedIds);
            await DeleteBatch(batchIds.Skip(mid).ToList(), totals, skippedIds);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Skipping undeletable metadata row {MetadataId} during cleanup",
                batchIds[0]
            );

            skippedIds.Add(batchIds[0]);
            totals.Skipped++;
        }
    }

    /// <summary>
    /// Test seam: passed to <see cref="DeleteUnreferencedAsync"/> as its
    /// <c>beforeMetadataDelete</c>, so a test can link a run while a sweep deletes its batch.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeMetadataDelete { get; set; }

    private async Task DeleteMetadataByIds(
        IReadOnlyList<long> batchIds,
        CleanupTotals totals,
        List<long> skippedIds
    )
    {
        var deleted = await DeleteUnreferencedAsync(
            dataContext,
            batchIds,
            CancellationToken,
            BeforeMetadataDelete,
            logger,
            leftOver: skippedIds
        );
        totals.WorkQueues += deleted.WorkQueues;
        totals.Logs += deleted.Logs;
        totals.Metadata += deleted.Metadata;
    }

    /// <summary>
    /// Deletes the runs among <paramref name="ids"/> that can go without leaving a kept run linked
    /// to a deleted one, with what they own, all or nothing. The batch is rechecked, then its owned
    /// rows and back-references are cleared and its runs deleted in one transaction, the delete
    /// repeating the keep test. When a queued entry or another run came to name one of the runs
    /// after the recheck, the delete keeps that run and the transaction is rolled back, so no kept
    /// run loses its work queue entry, logs, dead letter link or children's parent link; the batch
    /// is then rechecked and tried again without it (docs/adr/0017). After
    /// <see cref="MaxDeleteAttempts"/> such attempts the batch is logged and left for a later sweep:
    /// its runs are added to <paramref name="leftOver"/>, which a sweep excludes from the batches it
    /// selects after this one.
    /// </summary>
    /// <param name="dataContext">The context to delete through.</param>
    /// <param name="ids">The runs selected for deletion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="beforeMetadataDelete">Test seam: awaited on every attempt, just before the runs are deleted.</param>
    /// <param name="logger">Where a batch left for a later sweep is reported.</param>
    /// <param name="leftOver">Receives the runs of a batch left for a later sweep.</param>
    internal static async Task<(int Metadata, int WorkQueues, int Logs)> DeleteUnreferencedAsync(
        IDataContext dataContext,
        IReadOnlyList<long> ids,
        CancellationToken ct,
        Func<CancellationToken, Task>? beforeMetadataDelete = null,
        ILogger? logger = null,
        List<long>? leftOver = null
    )
    {
        var remaining = ids.ToList();

        for (var attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
        {
            var kept = await KeptAsync(dataContext, remaining, ct);
            remaining.RemoveAll(kept.Contains);

            if (remaining.Count == 0)
                return (0, 0, 0);

            if (
                await TryDeleteAllAsync(dataContext, remaining, beforeMetadataDelete, ct) is
                { } deleted
            )
                return deleted;
        }

        logger?.LogWarning(
            "Metadata cleanup left {Count} expired runs for a later sweep: a run in the batch was "
                + "linked for replay during each of {Attempts} attempts to delete it",
            remaining.Count,
            MaxDeleteAttempts
        );
        leftOver?.AddRange(remaining);
        return (0, 0, 0);
    }

    /// <summary>How many times a batch is rechecked and retried before it is left for a later sweep.</summary>
    internal const int MaxDeleteAttempts = 3;

    /// <summary>
    /// The runs among <paramref name="ids"/> that must stay, followed to a fixed point: a run named
    /// by a queued entry, by a run outside the set, or that replays a run that exists outside the
    /// set. Dropping one can make another stay, so it is repeated until nothing changes.
    /// </summary>
    private static async Task<System.Collections.Generic.HashSet<long>> KeptAsync(
        IDataContext dataContext,
        List<long> ids,
        CancellationToken ct
    )
    {
        var kept = new System.Collections.Generic.HashSet<long>();
        var remaining = ids.ToList();

        while (remaining.Count > 0)
        {
            var deletable = await Deletable(dataContext, remaining)
                .Select(m => m.Id)
                .ToListAsync(ct);
            if (deletable.Count == remaining.Count)
                break;

            kept.UnionWith(remaining.Except(deletable));
            remaining = deletable;
        }

        return kept;
    }

    /// <summary>
    /// The runs among <paramref name="ids"/> that no queued entry and no run outside
    /// <paramref name="ids"/> names in <c>replay_decisions_of</c>, and whose own
    /// <c>replay_decisions_of</c>, if set, names a run in <paramref name="ids"/> or one already gone.
    /// </summary>
    private static IQueryable<Effect.Models.Metadata.Metadata> Deletable(
        IDataContext dataContext,
        List<long> ids
    ) =>
        dataContext.Metadatas.Where(m =>
            ids.Contains(m.Id)
            && !dataContext.WorkQueues.Any(q =>
                q.ReplayDecisionsOf == m.Id && q.Status == WorkQueueStatus.Queued
            )
            && !dataContext.Metadatas.Any(r => r.ReplayDecisionsOf == m.Id && !ids.Contains(r.Id))
            && (
                m.ReplayDecisionsOf == null
                || ids.Contains(m.ReplayDecisionsOf.Value)
                || !dataContext.Metadatas.Any(s => s.Id == m.ReplayDecisionsOf)
            )
            && !dataContext.WorkQueues.Any(q =>
                q.ResumeFrom == m.Id && q.Status == WorkQueueStatus.Queued
            )
            && !dataContext.Metadatas.Any(r => r.ResumeFrom == m.Id && !ids.Contains(r.Id))
            && (
                m.ResumeFrom == null
                || ids.Contains(m.ResumeFrom.Value)
                || !dataContext.Metadatas.Any(s => s.Id == m.ResumeFrom)
            )
        );

    /// <summary>
    /// Deletes every run in <paramref name="ids"/> with what it owns, in one transaction, or
    /// nothing when the keep test spares any of them. Null when it rolled back.
    /// </summary>
    private static async Task<(int Metadata, int WorkQueues, int Logs)?> TryDeleteAllAsync(
        IDataContext dataContext,
        List<long> ids,
        Func<CancellationToken, Task>? beforeMetadataDelete,
        CancellationToken ct
    )
    {
        var database = ((DbContext)dataContext).Database;

        // Inside a caller's transaction the batch gets a savepoint instead of its own.
        var outer = database.CurrentTransaction;
        var own = outer is null ? await database.BeginTransactionAsync(ct) : null;
        const string savepoint = "trax_metadata_cleanup_batch";
        if (outer is not null)
            await outer.CreateSavepointAsync(savepoint, ct);

        try
        {
            // Work queue entries and logs are owned by the metadata and deleted outright.
            var workQueues = await dataContext
                .WorkQueues.Where(wq => wq.MetadataId.HasValue && ids.Contains(wq.MetadataId.Value))
                .ExecuteDeleteAsync(ct);

            var logs = await dataContext
                .Logs.Where(l => ids.Contains(l.MetadataId))
                .ExecuteDeleteAsync(ct);

            // Dead letters and child metadata reference the metadata but are not owned by it: a
            // dead letter is a meaningful record and a child train's metadata can outlive its
            // parent. Null the back-references so the FK does not block the delete, rather than
            // cascading into them.
            await dataContext
                .DeadLetters.Where(d =>
                    d.RetryMetadataId.HasValue && ids.Contains(d.RetryMetadataId.Value)
                )
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryMetadataId, (long?)null), ct);

            await dataContext
                .Metadatas.Where(c => c.ParentId.HasValue && ids.Contains(c.ParentId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentId, (long?)null), ct);

            // An entry that is no longer queued and resumes one of these runs, a cancelled one or
            // one dispatched as a run that is not this batch's, will never read its link again: a
            // dispatched entry's run carries its own. Its link is cleared, so nothing names a run
            // that is gone (Trax.Docs/adr/0047). A queued one keeps the run (the keep test).
            await dataContext
                .WorkQueues.Where(q =>
                    q.ResumeFrom.HasValue
                    && ids.Contains(q.ResumeFrom.Value)
                    && q.Status != WorkQueueStatus.Queued
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(q => q.ResumeFrom, (long?)null)
                            .SetProperty(q => q.ResumeAt, (string?)null),
                    ct
                );

            if (beforeMetadataDelete is not null)
                await beforeMetadataDelete(ct);

            var metadata = await Deletable(dataContext, ids).ExecuteDeleteAsync(ct);

            if (metadata == ids.Count)
            {
                if (own is not null)
                    await own.CommitAsync(ct);
                else
                    await outer!.ReleaseSavepointAsync(savepoint, ct);
                return (metadata, workQueues, logs);
            }

            // A run was linked after the recheck: undo the batch so it keeps everything it owns.
            if (own is not null)
                await own.RollbackAsync(ct);
            else
                await outer!.RollbackToSavepointAsync(savepoint, ct);
            return null;
        }
        catch
        {
            if (own is not null)
                await own.RollbackAsync(CancellationToken.None);
            else
                await outer!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync();
        }
    }

    private sealed class CleanupTotals
    {
        public int Metadata { get; set; }
        public int WorkQueues { get; set; }
        public int Logs { get; set; }
        public int Skipped { get; set; }
    }
}
