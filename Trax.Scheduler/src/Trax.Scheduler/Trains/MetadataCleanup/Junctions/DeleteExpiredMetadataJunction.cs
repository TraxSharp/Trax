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
/// Uses EF Core bulk delete (<see cref="RelationalQueryableExtensions.ExecuteDeleteAsync{TSource}"/>)
/// for efficient single-statement SQL deletion without loading entities into memory.
///
/// Only metadata in a terminal state (Completed or Failed) is eligible for deletion.
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
        var whitelist = TrainNameExpander
            .ExpandTrainNames(cleanupConfig.TrainTypeWhitelist, discoveryService)
            .ToList();
        var cutoffTime = DateTime.UtcNow - cleanupConfig.RetentionPeriod;

        logger.LogDebug(
            "Deleting metadata older than {CutoffTime} for train types [{Whitelist}]",
            cutoffTime,
            string.Join(", ", whitelist)
        );

        // Build the set of metadata IDs to delete (terminal state, matching whitelist, expired)
        var metadataIdsToDelete = dataContext
            .Metadatas.Where(m => whitelist.Contains(m.Name))
            .Where(m => m.StartTime < cutoffTime)
            .Where(m =>
                m.TrainState == TrainState.Completed
                || m.TrainState == TrainState.Failed
                || m.TrainState == TrainState.Cancelled
            )
            .Select(m => m.Id);

        // Delete associated work queue entries first to avoid FK constraint violations
        var workQueuesDeleted = await dataContext
            .WorkQueues.Where(wq =>
                wq.MetadataId.HasValue && metadataIdsToDelete.Contains(wq.MetadataId.Value)
            )
            .ExecuteDeleteAsync(CancellationToken);

        // Delete associated logs to avoid FK constraint violations
        var logsDeleted = await dataContext
            .Logs.Where(l => metadataIdsToDelete.Contains(l.MetadataId))
            .ExecuteDeleteAsync(CancellationToken);

        // Delete the metadata rows
        var metadataDeleted = await dataContext
            .Metadatas.Where(m => whitelist.Contains(m.Name))
            .Where(m => m.StartTime < cutoffTime)
            .Where(m =>
                m.TrainState == TrainState.Completed
                || m.TrainState == TrainState.Failed
                || m.TrainState == TrainState.Cancelled
            )
            .ExecuteDeleteAsync(CancellationToken);

        if (metadataDeleted > 0)
        {
            logger.LogInformation(
                "Metadata cleanup completed: deleted {MetadataCount} metadata, {WorkQueueCount} work queue entries, and {LogCount} log entries",
                metadataDeleted,
                workQueuesDeleted,
                logsDeleted
            );
        }
        else
        {
            logger.LogDebug("Metadata cleanup completed: no expired entries found");
        }

        return Unit.Default;
    }
}
