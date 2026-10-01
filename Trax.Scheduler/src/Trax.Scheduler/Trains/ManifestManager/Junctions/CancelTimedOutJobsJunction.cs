using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Cancels running jobs that have exceeded their configured timeout.
/// </summary>
/// <remarks>
/// Selects every InProgress run directly, not through the loaded manifests, so a run without a
/// manifest (queued or run through the operations surface) and a run of a manifest disabled while
/// it runs are both covered. A run's timeout comes from the run at the root of its ParentId chain
/// (see <see cref="RunTimeouts"/>): the root manifest's TimeoutSeconds when it has one, and the
/// global DefaultJobTimeout when a scheduler dispatched the root. A train nested inside a scheduled
/// run therefore shares the scheduled run's timeout, and a run started on the train bus by a host
/// sharing the database, which no scheduler dispatched, is not timed out here. The scheduler's own
/// trains (<see cref="AdminTrains"/>) and the host's ExcludedTrainTypeNames are left alone: a
/// JobRunner run lasts as long as the run it executes, which has its own timeout. For each
/// timed-out run, sets
/// CancellationRequested=true in the database and attempts same-server instant cancellation via
/// ICancellationRegistry.
///
/// Cancelled jobs transition to TrainState.Cancelled at the next junction boundary
/// (via CancellationCheckProvider) or immediately (via CTS). A cancelled run is not retried and
/// does not create a dead letter: it consumes the occurrence it ran for, so a scheduled manifest
/// next runs at its next scheduled occurrence.
///
/// This junction runs first in the ManifestManagerTrain chain, before the manifests are loaded.
/// </remarks>
internal class CancelTimedOutJobsJunction(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ICancellationRegistry cancellationRegistry,
    ILogger<CancelTimedOutJobsJunction> logger
) : EffectJunction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        var now = DateTime.UtcNow;
        var adminTrainNames = AdminTrains.FullNames.ToList();

        var inProgressMetadata = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.InProgress
                && !m.CancellationRequested
                && !adminTrainNames.Contains(m.Name)
                && !config.ExcludedTrainTypeNames.Contains(m.Name)
            )
            .Select(m => new TimedRun(
                m.Id,
                m.ParentId,
                m.Name,
                m.StartTime,
                m.ManifestId,
                m.Manifest != null ? m.Manifest.TimeoutSeconds : null
            ))
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        if (inProgressMetadata.Count == 0)
        {
            logger.LogDebug("CancelTimedOutJobsJunction: no in-progress runs to check");
            return Unit.Default;
        }

        var bounds = await RunTimeouts.ResolveAsync(
            dataContext,
            config,
            inProgressMetadata,
            CancellationToken
        );

        var metadataIdsToCancel = new List<long>();

        foreach (var md in inProgressMetadata)
        {
            var bound = bounds[md.Id];
            if (bound.Excluded || bound.Timeout is not { } timeout)
                continue;

            var elapsed = now - md.StartTime;

            if (elapsed > timeout)
            {
                metadataIdsToCancel.Add(md.Id);
                logger.LogWarning(
                    "Metadata {MetadataId} (Manifest {ManifestId}) timed out: elapsed {Elapsed} > timeout {Timeout}",
                    md.Id,
                    bound.RootManifestId,
                    elapsed,
                    timeout
                );
            }
        }

        if (metadataIdsToCancel.Count == 0)
        {
            logger.LogDebug("CancelTimedOutJobsJunction: no timed-out jobs found");
            return Unit.Default;
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
            "CancelTimedOutJobsJunction completed: {CancelledCount} timed-out job(s) cancelled",
            metadataIdsToCancel.Count
        );

        return Unit.Default;
    }
}
