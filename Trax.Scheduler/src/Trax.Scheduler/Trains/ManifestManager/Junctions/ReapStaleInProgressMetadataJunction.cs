using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Fails InProgress metadata that has not completed within the configured timeout.
/// </summary>
/// <remarks>
/// Acts as a safety net for worker crashes, Lambda hard-kills, or OOM events where the
/// process dies without reaching FinishServiceTrain. If a job remains in InProgress state
/// longer than <see cref="SchedulerConfiguration.StaleInProgressTimeout"/>, this junction
/// will mark the metadata as Failed so it doesn't stay orphaned and block the
/// DormantDependentContext concurrency guard or count against MaxActiveJobs capacity.
///
/// This junction runs after ReapStalePendingMetadataJunction and before ReapFailedJobsJunction
/// so that newly-failed metadata is visible to the reaper in the same ManifestManager cycle
/// (enabling dead-lettering if retries are exhausted).
/// </remarks>
internal class ReapStaleInProgressMetadataJunction(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ILogger<ReapStaleInProgressMetadataJunction> logger
) : EffectJunction<List<ManifestDispatchView>, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(List<ManifestDispatchView> views)
    {
        var cutoff = DateTime.UtcNow - config.StaleInProgressTimeout;

        var staleMetadata = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.InProgress
                && m.StartTime < cutoff
                && !config.ExcludedTrainTypeNames.Contains(m.Name)
            )
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.StartTime,
                m.ManifestId,
            })
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        if (staleMetadata.Count == 0)
        {
            logger.LogDebug(
                "ReapStaleInProgressMetadataJunction: no stale in-progress metadata found"
            );
            return views;
        }

        var staleIds = new List<long>(staleMetadata.Count);

        foreach (var md in staleMetadata)
        {
            staleIds.Add(md.Id);
            logger.LogWarning(
                "Metadata {MetadataId} (train: {TrainName}, manifest: {ManifestId}) "
                    + "has been InProgress since {StartTime} — marking as failed",
                md.Id,
                md.Name,
                md.ManifestId,
                md.StartTime
            );
        }

        var now = DateTime.UtcNow;

        await dataContext
            .Metadatas.Where(m => staleIds.Contains(m.Id) && m.TrainState == TrainState.InProgress)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.TrainState, TrainState.Failed)
                        .SetProperty(m => m.EndTime, now)
                        .SetProperty(
                            m => m.FailureReason,
                            "Job was stuck InProgress beyond the configured stale in-progress timeout"
                        )
                        .SetProperty(m => m.FailureException, "StaleInProgressTimeout")
                        .SetProperty(
                            m => m.FailureJunction,
                            nameof(ReapStaleInProgressMetadataJunction)
                        ),
                CancellationToken
            );

        logger.LogInformation(
            "ReapStaleInProgressMetadataJunction completed: {Count} stale in-progress job(s) marked as failed",
            staleIds.Count
        );

        return views;
    }
}
