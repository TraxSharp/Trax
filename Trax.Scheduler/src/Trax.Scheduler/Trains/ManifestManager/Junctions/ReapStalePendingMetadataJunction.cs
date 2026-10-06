using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Fails Pending metadata that has not been picked up within the configured timeout.
/// </summary>
/// <remarks>
/// Acts as a safety net for dispatch failures — if a job was dispatched but the worker
/// never started executing it (e.g. remote worker unreachable, Lambda crashed after
/// receiving the request, or the immediate failure handler in DispatchJobsJunction also failed),
/// this junction will mark the metadata as Failed so it doesn't stay orphaned in Pending state
/// forever and count against MaxActiveJobs capacity.
///
/// A run whose job still has a row in <c>trax.background_job</c> is not reaped: it was delivered
/// to the local worker pool and is waiting for a free worker, or is being run by one. Failing it
/// would record a failure that did not happen, and the worker that reaches it would then find it
/// no longer Pending and not run it. A job whose worker died is recovered by the worker pool
/// itself, after <see cref="LocalWorkerOptions.VisibilityTimeout"/>.
///
/// This junction runs before LoadManifestsJunction so that newly-failed metadata is counted in
/// the same ManifestManager cycle (enabling dead-lettering if retries are exhausted).
/// </remarks>
internal class ReapStalePendingMetadataJunction(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ILogger<ReapStalePendingMetadataJunction> logger
) : EffectJunction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        var cutoff = DateTime.UtcNow - config.StalePendingTimeout;

        var staleMetadata = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.Pending
                && m.StartTime < cutoff
                && !config.ExcludedTrainTypeNames.Contains(m.Name)
                && !dataContext.BackgroundJobs.Any(j => j.MetadataId == m.Id)
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
            logger.LogDebug("ReapStalePendingMetadataJunction: no stale pending metadata found");
            return Unit.Default;
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
            .Metadatas.Where(m =>
                staleIds.Contains(m.Id)
                && m.TrainState == TrainState.Pending
                && !dataContext.BackgroundJobs.Any(j => j.MetadataId == m.Id)
            )
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.TrainState, TrainState.Failed)
                        .SetProperty(m => m.EndTime, now)
                        .SetProperty(
                            m => m.FailureReason,
                            "Job was not picked up within the configured stale pending timeout"
                        )
                        .SetProperty(m => m.FailureException, "StalePendingTimeout")
                        .SetProperty(
                            m => m.FailureJunction,
                            nameof(ReapStalePendingMetadataJunction)
                        ),
                CancellationToken
            );

        logger.LogInformation(
            "ReapStalePendingMetadataJunction completed: {Count} stale pending job(s) marked as failed",
            staleIds.Count
        );

        return Unit.Default;
    }
}
