using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectStep;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Steps;

/// <summary>
/// Fails Pending metadata that has not been picked up within the configured timeout.
/// </summary>
/// <remarks>
/// Acts as a safety net for dispatch failures — if a job was dispatched but the worker
/// never started executing it (e.g. remote worker unreachable, Lambda crashed after
/// receiving the request, or the immediate failure handler in DispatchJobsStep also failed),
/// this step will mark the metadata as Failed so it doesn't stay orphaned in Pending state
/// forever and count against MaxActiveJobs capacity.
///
/// This step runs before ReapFailedJobsStep so that newly-failed metadata is visible to
/// the reaper in the same ManifestManager cycle (enabling dead-lettering if retries are exhausted).
/// </remarks>
internal class ReapStalePendingMetadataStep(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ILogger<ReapStalePendingMetadataStep> logger
) : EffectStep<List<ManifestDispatchView>, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(List<ManifestDispatchView> views)
    {
        var cutoff = DateTime.UtcNow - config.StalePendingTimeout;

        var staleMetadata = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.Pending
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
            logger.LogDebug("ReapStalePendingMetadataStep: no stale pending metadata found");
            return views;
        }

        var staleIds = new List<long>(staleMetadata.Count);

        foreach (var md in staleMetadata)
        {
            staleIds.Add(md.Id);
            logger.LogWarning(
                "Metadata {MetadataId} (train: {TrainName}, manifest: {ManifestId}) "
                    + "has been Pending since {StartTime} — marking as failed",
                md.Id,
                md.Name,
                md.ManifestId,
                md.StartTime
            );
        }

        var now = DateTime.UtcNow;

        await dataContext
            .Metadatas.Where(m => staleIds.Contains(m.Id) && m.TrainState == TrainState.Pending)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.TrainState, TrainState.Failed)
                        .SetProperty(m => m.EndTime, now)
                        .SetProperty(
                            m => m.FailureReason,
                            "Job was not picked up within the configured stale pending timeout"
                        ),
                CancellationToken
            );

        logger.LogInformation(
            "ReapStalePendingMetadataStep completed: {Count} stale pending job(s) marked as failed",
            staleIds.Count
        );

        return views;
    }
}
