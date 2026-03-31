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
/// Each batch loads metadata IDs first, then deletes associated FK rows and metadata by ID.
/// The junction loops until no more expired rows remain.
///
/// Only metadata in a terminal state (Completed, Failed, or Cancelled) is eligible for deletion.
/// Associated work queue entries and log entries are deleted first to avoid foreign key constraint violations.
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
        var cutoffTime = DateTime.UtcNow - cleanupConfig.RetentionPeriod;
        var batchSize = cleanupConfig.DeleteBatchSize;

        logger.LogDebug(
            "Deleting metadata older than {CutoffTime} for train types [{Whitelist}]",
            cutoffTime,
            string.Join(", ", whitelist)
        );

        var totalMetadataDeleted = 0;
        var totalWorkQueuesDeleted = 0;
        var totalLogsDeleted = 0;

        while (true)
        {
            // Load a batch of metadata IDs to delete
            var query = dataContext
                .Metadatas.Where(m => whitelist.Contains(m.Name))
                .Where(m => m.StartTime < cutoffTime)
                .Where(m =>
                    m.TrainState == TrainState.Completed
                    || m.TrainState == TrainState.Failed
                    || m.TrainState == TrainState.Cancelled
                )
                .Select(m => m.Id);

            var batchIds = batchSize.HasValue
                ? await query.Take(batchSize.Value).ToListAsync(CancellationToken)
                : await query.ToListAsync(CancellationToken);

            if (batchIds.Count == 0)
                break;

            // Delete associated work queue entries first to avoid FK constraint violations
            totalWorkQueuesDeleted += await dataContext
                .WorkQueues.Where(wq =>
                    wq.MetadataId.HasValue && batchIds.Contains(wq.MetadataId.Value)
                )
                .ExecuteDeleteAsync(CancellationToken);

            // Delete associated logs to avoid FK constraint violations
            totalLogsDeleted += await dataContext
                .Logs.Where(l => batchIds.Contains(l.MetadataId))
                .ExecuteDeleteAsync(CancellationToken);

            // Delete the metadata rows
            totalMetadataDeleted += await dataContext
                .Metadatas.Where(m => batchIds.Contains(m.Id))
                .ExecuteDeleteAsync(CancellationToken);

            // If no batch limit or batch was smaller than limit, we're done
            if (!batchSize.HasValue || batchIds.Count < batchSize.Value)
                break;
        }

        if (totalMetadataDeleted > 0)
        {
            logger.LogInformation(
                "Metadata cleanup completed: deleted {MetadataCount} metadata, {WorkQueueCount} work queue entries, and {LogCount} log entries",
                totalMetadataDeleted,
                totalWorkQueuesDeleted,
                totalLogsDeleted
            );
        }
        else
        {
            logger.LogDebug("Metadata cleanup completed: no expired entries found");
        }

        return Unit.Default;
    }
}
