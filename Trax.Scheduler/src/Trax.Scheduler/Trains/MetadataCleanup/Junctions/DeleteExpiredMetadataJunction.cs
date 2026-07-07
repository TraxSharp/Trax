using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

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
/// Only metadata in a terminal state (Completed, Failed, or Cancelled) is eligible for deletion.
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
        var whitelist = TrainNameExpander.ExpandTrainNames(
            cleanupConfig.TrainTypeWhitelist,
            discoveryService
        );

        // Internal scheduler trains are the highest-volume metadata writers and are pruned
        // unconditionally so a consumer can never forget one and let the table grow unbounded.
        foreach (var adminName in AdminTrains.FullNames)
            whitelist.Add(adminName);

        var cutoffTime = DateTime.UtcNow - cleanupConfig.RetentionPeriod;
        var batchSize = cleanupConfig.DeleteBatchSize;

        logger.LogDebug(
            "Deleting metadata older than {CutoffTime} for train types [{Whitelist}]",
            cutoffTime,
            string.Join(", ", whitelist)
        );

        var totals = new CleanupTotals();

        // Rows that could not be deleted are excluded from later batches so the sweep makes
        // progress instead of re-selecting the same poison rows forever.
        var skippedIds = new List<long>();

        while (true)
        {
            var query = dataContext
                .Metadatas.Where(m => whitelist.Contains(m.Name))
                .Where(m => m.StartTime < cutoffTime)
                .Where(m =>
                    m.TrainState == TrainState.Completed
                    || m.TrainState == TrainState.Failed
                    || m.TrainState == TrainState.Cancelled
                )
                .Where(m => !skippedIds.Contains(m.Id))
                .Select(m => m.Id);

            var batchIds = batchSize.HasValue
                ? await query.Take(batchSize.Value).ToListAsync(CancellationToken)
                : await query.ToListAsync(CancellationToken);

            if (batchIds.Count == 0)
                break;

            await DeleteBatch(batchIds, totals, skippedIds);

            // No batch limit means we processed everything eligible in one pass.
            if (!batchSize.HasValue || batchIds.Count < batchSize.Value)
                break;
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
            await DeleteMetadataByIds(batchIds, totals);
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

    private async Task DeleteMetadataByIds(IReadOnlyList<long> batchIds, CleanupTotals totals)
    {
        var ids = batchIds.ToList();

        // Work queue entries and logs are owned by the metadata and deleted outright.
        totals.WorkQueues += await dataContext
            .WorkQueues.Where(wq => wq.MetadataId.HasValue && ids.Contains(wq.MetadataId.Value))
            .ExecuteDeleteAsync(CancellationToken);

        totals.Logs += await dataContext
            .Logs.Where(l => ids.Contains(l.MetadataId))
            .ExecuteDeleteAsync(CancellationToken);

        // Dead letters and child metadata reference the metadata but are not owned by it: a dead
        // letter is a meaningful record and a child train's metadata can outlive its parent. Null
        // the back-references so the FK does not block the delete, rather than cascading into them.
        await dataContext
            .DeadLetters.Where(d =>
                d.RetryMetadataId.HasValue && ids.Contains(d.RetryMetadataId.Value)
            )
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.RetryMetadataId, (long?)null),
                CancellationToken
            );

        await dataContext
            .Metadatas.Where(m => m.ParentId.HasValue && ids.Contains(m.ParentId.Value))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.ParentId, (long?)null),
                CancellationToken
            );

        totals.Metadata += await dataContext
            .Metadatas.Where(m => ids.Contains(m.Id))
            .ExecuteDeleteAsync(CancellationToken);
    }

    private sealed class CleanupTotals
    {
        public int Metadata { get; set; }
        public int WorkQueues { get; set; }
        public int Logs { get; set; }
        public int Skipped { get; set; }
    }
}
