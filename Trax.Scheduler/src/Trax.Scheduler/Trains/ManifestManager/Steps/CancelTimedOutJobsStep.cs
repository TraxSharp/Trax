using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectStep;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;

namespace Trax.Scheduler.Trains.ManifestManager.Steps;

/// <summary>
/// Cancels running jobs that have exceeded their configured timeout.
/// </summary>
/// <remarks>
/// Queries InProgress metadata where the elapsed time since StartTime exceeds the manifest's
/// TimeoutSeconds (or the global DefaultJobTimeout). For each timed-out job, sets
/// CancellationRequested=true in the database and attempts same-server instant cancellation
/// via ICancellationRegistry.
///
/// Cancelled jobs transition to TrainState.Cancelled at the next step boundary
/// (via CancellationCheckProvider) or immediately (via CTS). Cancelled jobs are
/// NOT retried and do NOT create dead letters.
///
/// This step runs before ReapFailedJobsStep in the ManifestManagerTrain chain.
/// </remarks>
internal class CancelTimedOutJobsStep(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ICancellationRegistry cancellationRegistry,
    ILogger<CancelTimedOutJobsStep> logger
) : EffectStep<List<ManifestDispatchView>, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(List<ManifestDispatchView> views)
    {
        var manifestIdsWithActiveJobs = views
            .Where(v => v.HasActiveExecution)
            .Select(v => v.Manifest.Id)
            .ToList();

        if (manifestIdsWithActiveJobs.Count == 0)
        {
            logger.LogDebug("CancelTimedOutJobsStep: no active executions to check");
            return views;
        }

        var now = DateTime.UtcNow;
        var defaultTimeoutSeconds = (int)config.DefaultJobTimeout.TotalSeconds;

        var inProgressMetadata = await dataContext
            .Metadatas.Where(m =>
                m.ManifestId != null
                && manifestIdsWithActiveJobs.Contains(m.ManifestId.Value)
                && m.TrainState == TrainState.InProgress
                && !m.CancellationRequested
            )
            .Select(m => new
            {
                m.Id,
                m.StartTime,
                m.ManifestId,
                TimeoutSeconds = m.Manifest != null ? m.Manifest.TimeoutSeconds : null,
            })
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        var metadataIdsToCancel = new List<long>();

        foreach (var md in inProgressMetadata)
        {
            var timeoutSeconds = md.TimeoutSeconds ?? defaultTimeoutSeconds;
            var elapsed = now - md.StartTime;

            if (elapsed > TimeSpan.FromSeconds(timeoutSeconds))
            {
                metadataIdsToCancel.Add(md.Id);
                logger.LogWarning(
                    "Metadata {MetadataId} (Manifest {ManifestId}) timed out: elapsed {Elapsed} > timeout {Timeout}s",
                    md.Id,
                    md.ManifestId,
                    elapsed,
                    timeoutSeconds
                );
            }
        }

        if (metadataIdsToCancel.Count == 0)
        {
            logger.LogDebug("CancelTimedOutJobsStep: no timed-out jobs found");
            return views;
        }

        await dataContext
            .Metadatas.Where(m => metadataIdsToCancel.Contains(m.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CancellationRequested, true),
                CancellationToken
            );

        foreach (var metadataId in metadataIdsToCancel)
        {
            var cancelled = cancellationRegistry.TryCancel(metadataId);
            if (cancelled)
                logger.LogDebug(
                    "Same-server instant cancel succeeded for Metadata {MetadataId}",
                    metadataId
                );
        }

        logger.LogInformation(
            "CancelTimedOutJobsStep completed: {CancelledCount} timed-out job(s) cancelled",
            metadataIdsToCancel.Count
        );

        return views;
    }
}
