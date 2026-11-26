using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

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
/// A run is reaped at the later of <see cref="SchedulerConfiguration.StaleInProgressTimeout"/> and
/// its own timeout plus the grace the defaults leave between the job timeout and the stale
/// timeout (<c>StaleInProgressTimeout - DefaultJobTimeout</c>, never negative). Its own timeout is
/// resolved as CancelTimedOutJobsJunction resolves it (see <see cref="RunTimeouts"/>): the
/// manifest timeout of the run at the root of its ParentId chain, or
/// <see cref="SchedulerConfiguration.DefaultJobTimeout"/> when a scheduler dispatched that root, so
/// a nested run is kept as long as the scheduled run it belongs to and a
/// <see cref="SchedulerConfiguration.DefaultJobTimeout"/> longer than the stale timeout is honoured. A run still inside its own
/// timeout is therefore never reaped while it runs, and one that overran it is first cancelled by
/// CancelTimedOutJobsJunction and only then, once cancellation has had the same time to land,
/// failed here.
///
/// A run of a train in <see cref="SchedulerConfiguration.ExcludedTrainTypeNames"/> (the scheduler's
/// own and every <c>ExcludeFromMaxActiveJobs</c> train) has no timeout, so it is judged by its host
/// instead: it is failed once it started more than <see cref="ExcludedOrphanedAfter"/> ago and its
/// host (<c>HostInstanceId</c>) has written no heartbeat since then. A run on a host that still
/// beats is never failed, however long it runs. A forced roll kills such a host, and a hub that
/// starts while drained skips the startup recovery that used to fail the run (Trax.Docs/adr/0065).
///
/// Each run it fails is published to the lifecycle hooks as <c>Failed</c>, once, after the write
/// commits (see <see cref="DeferredOutcomeEvents"/>), since no train is left to publish it.
///
/// This junction runs after ReapStalePendingMetadataJunction and before LoadManifestsJunction
/// so that newly-failed metadata is counted in the same ManifestManager cycle (enabling
/// dead-lettering if retries are exhausted).
/// </remarks>
internal sealed class ReapStaleInProgressMetadataJunction(
    IDataContext dataContext,
    SchedulerConfiguration config,
    DeferredOutcomeEvents outcomeEvents,
    ILogger<ReapStaleInProgressMetadataJunction> logger,
    TimeProvider timeProvider
) : EffectJunction<Unit, Unit>
{
    /// <summary>
    /// How long an excluded train's run must have run, and its host gone without a heartbeat, before
    /// it is failed: a deploy's default drain timeout plus its margin, 20 minutes. A live host beats
    /// every 15 seconds.
    /// </summary>
    internal static readonly TimeSpan ExcludedOrphanedAfter =
        TraxDrain.DefaultTimeout + TraxDrain.Margin;

    public override async Task<Unit> Run(Unit input)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ReapOrphanedExcludedRunsAsync(now).ConfigureAwait(false);
        var staleMetadata = await FindStaleRunsAsync(now).ConfigureAwait(false);

        if (staleMetadata.Count == 0)
        {
            logger.LogDebug(
                "ReapStaleInProgressMetadataJunction: no stale in-progress metadata found"
            );
            return Unit.Default;
        }

        var staleIds = StaleIds(staleMetadata);

        // One conditional write for every run, which also tells the runs it moved to Failed: a run
        // that finished, or that another pass reaped, matches nothing and is not published again.
        var reaped = await ReapedRuns
            .FailAsync(
                dataContext,
                dataContext.Metadatas.Where(m =>
                    staleIds.Contains(m.Id) && m.TrainState == TrainState.InProgress
                ),
                staleIds,
                "Job was stuck InProgress beyond the configured stale in-progress timeout",
                "StaleInProgressTimeout",
                nameof(ReapStaleInProgressMetadataJunction),
                timeProvider,
                CancellationToken
            )
            .ConfigureAwait(false);

        logger.LogInformation(
            "ReapStaleInProgressMetadataJunction completed: {Count} stale in-progress job(s) marked as failed",
            reaped.Count
        );

        // No train ran to publish these outcomes; published once the write commits.
        await outcomeEvents.FailedAsync(dataContext, reaped).ConfigureAwait(false);

        return Unit.Default;
    }

    /// <summary>
    /// Fails the excluded trains' runs whose host has stopped beating (see the remarks), each
    /// logged and published like the stale ones.
    /// </summary>
    private async Task ReapOrphanedExcludedRunsAsync(DateTime now)
    {
        if (config.ExcludedTrainTypeNames.Count == 0)
            return;
        var cutoff = now - ExcludedOrphanedAfter;
        var orphans = dataContext.Metadatas.Where(m =>
            m.TrainState == TrainState.InProgress
            && m.StartTime < cutoff
            && config.ExcludedTrainTypeNames.Contains(m.Name)
            && !dataContext.HostHeartbeats.Any(h =>
                h.InstanceId == m.HostInstanceId && h.LastHeartbeatAt >= cutoff
            )
        );
        var found = await orphans
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.StartTime,
                m.HostInstanceId,
            })
            .AsNoTracking()
            .ToListAsync(CancellationToken)
            .ConfigureAwait(false);
        if (found.Count == 0)
            return;
        foreach (var md in found)
            logger.LogWarning(
                "Metadata {MetadataId} (train: {TrainName}) has been InProgress since {StartTime} on "
                    + "host {Host}, which has not beaten since {Cutoff} — marking as failed",
                md.Id,
                md.Name,
                md.StartTime,
                md.HostInstanceId,
                cutoff
            );
        var ids = found.Select(m => m.Id).ToList();
        var reaped = await ReapedRuns
            .FailAsync(
                dataContext,
                orphans.Where(m => ids.Contains(m.Id)),
                ids,
                "The host running this train stopped beating while it was InProgress",
                "HostStoppedBeating",
                nameof(ReapStaleInProgressMetadataJunction),
                timeProvider,
                CancellationToken
            )
            .ConfigureAwait(false);
        await outcomeEvents.FailedAsync(dataContext, reaped).ConfigureAwait(false);
    }

    private List<long> StaleIds(List<TimedRun> staleMetadata)
    {
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

        return staleIds;
    }

    private async Task<List<TimedRun>> FindStaleRunsAsync(DateTime now)
    {
        var cutoff = now - config.StaleInProgressTimeout;

        // Candidates are past the global stale timeout; a run whose manifest allows it longer
        // is kept until its own threshold.
        var candidates = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.InProgress
                && m.StartTime < cutoff
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
            .ToListAsync(CancellationToken)
            .ConfigureAwait(false);

        var bounds =
            candidates.Count == 0
                ? []
                : await RunTimeouts
                    .ResolveAsync(dataContext, config, candidates, CancellationToken)
                    .ConfigureAwait(false);

        var staleMetadata = candidates
            .Where(m => now - m.StartTime > StaleThreshold(bounds[m.Id].Timeout))
            .ToList();

        return staleMetadata;
    }

    /// <summary>
    /// How long a run may stay InProgress before it is failed: the stale in-progress timeout, or
    /// the run's own timeout plus the grace between the default job timeout and the stale timeout
    /// when that is longer.
    /// </summary>
    private TimeSpan StaleThreshold(TimeSpan? effectiveTimeout)
    {
        if (effectiveTimeout is not { } timeout)
            return config.StaleInProgressTimeout;

        var grace = config.StaleInProgressTimeout - config.DefaultJobTimeout;
        if (grace < TimeSpan.Zero)
            grace = TimeSpan.Zero;

        var ownThreshold = timeout + grace;
        return ownThreshold > config.StaleInProgressTimeout
            ? ownThreshold
            : config.StaleInProgressTimeout;
    }
}
