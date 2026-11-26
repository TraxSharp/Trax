using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Drain;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Determines which manifests are due for execution based on their scheduling rules.
/// </summary>
/// <remarks>
/// MaxActiveJobs is NOT enforced here — that responsibility belongs to the JobDispatcher,
/// which is the single gateway to the BackgroundTaskServer. This junction freely identifies
/// all manifests that are due, applying only per-manifest guards (dead letters, duplicate
/// queue entries, active executions).
/// </remarks>
internal sealed class DetermineJobsToQueueJunction(
    ILogger<DetermineJobsToQueueJunction> logger,
    SchedulerConfiguration config,
    TimeProvider timeProvider,
    IServiceProvider services
) : EffectJunction<(List<ManifestDispatchView>, List<DeadLetter>), List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(
        (List<ManifestDispatchView>, List<DeadLetter>) input
    )
    {
        var (views, newlyCreatedDeadLetters) = input;

        logger.LogDebug(
            "Starting DetermineJobsToQueueJunction to identify manifests due for execution"
        );

        // A drain stops every host queuing scheduled work (Trax.Docs/adr/0065). Nothing due now is
        // queued, and a schedule that came due meanwhile is a misfire once the drain ends: its
        // manifest's misfire policy decides whether it runs then. The reapers before this junction
        // keep running, so a run past its timeout still ends while drained.
        if (await SchedulerDrain.IsDrainedAsync(services, CancellationToken).ConfigureAwait(false))
        {
            logger.LogDebug("Drained: the ManifestManager queues no scheduled run this cycle");
            return [];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var viewsToQueue = new List<ManifestDispatchView>();

        // Create a set of manifest IDs that were just dead-lettered in this cycle
        var newlyDeadLetteredManifestIds = newlyCreatedDeadLetters
            .Select(dl => dl.ManifestId)
            .ToHashSet();

        AddDueScheduled(views, newlyDeadLetteredManifestIds, now, viewsToQueue);

        AddDueDependents(views, newlyDeadLetteredManifestIds, viewsToQueue);

        if (viewsToQueue.Count > 0)
            logger.LogInformation(
                "DetermineJobsToQueueJunction completed: {ManifestsToQueueCount} manifests are due for execution",
                viewsToQueue.Count
            );
        else
            logger.LogDebug(
                "DetermineJobsToQueueJunction completed: no manifests due for execution"
            );

        return viewsToQueue;
    }

    private void AddDueScheduled(
        List<ManifestDispatchView> views,
        HashSet<long> newlyDeadLetteredManifestIds,
        DateTime now,
        List<ManifestDispatchView> viewsToQueue
    )
    {
        // Filter to only time-based scheduled manifests (not manual-only or dependent)
        // Also skip manifests whose group is disabled
        var scheduledViews = views
            .Where(v =>
                v.Manifest.ScheduleType != ScheduleType.None
                && v.Manifest.ScheduleType != ScheduleType.Dependent
                && v.Manifest.ScheduleType != ScheduleType.DormantDependent
                && v.ManifestGroup.IsEnabled
            )
            .ToList();

        logger.LogDebug(
            "Found {ManifestCount} enabled scheduled manifests to evaluate",
            scheduledViews.Count
        );

        foreach (var view in scheduledViews)
        {
            if (ShouldSkipManifest(view, newlyDeadLetteredManifestIds))
                continue;

            // Check if this manifest is due for execution
            if (
                SchedulingHelpers.ShouldRunNow(
                    view.Manifest,
                    now,
                    config,
                    logger,
                    view.LastCancelledRun
                )
            )
            {
                logger.LogDebug(
                    "Manifest {ManifestId} (name: {ManifestName}) is due for execution",
                    view.Manifest.Id,
                    view.Manifest.Name
                );
                viewsToQueue.Add(view);
            }
        }
    }

    private void AddDueDependents(
        List<ManifestDispatchView> views,
        HashSet<long> newlyDeadLetteredManifestIds,
        List<ManifestDispatchView> viewsToQueue
    )
    {
        // Evaluate dependent manifests (triggered by parent success, not by schedule)
        var dependentViews = views
            .Where(v =>
                v.Manifest.ScheduleType == ScheduleType.Dependent
                && v.Manifest.DependsOnManifestId != null
                && v.ManifestGroup.IsEnabled
            )
            .ToList();

        if (dependentViews.Count > 0)
        {
            logger.LogDebug(
                "Found {DependentCount} dependent manifests to evaluate",
                dependentViews.Count
            );

            foreach (var dependent in dependentViews)
            {
                if (ShouldSkipManifest(dependent, newlyDeadLetteredManifestIds))
                    continue;

                if (IsDependentDue(dependent, views))
                    viewsToQueue.Add(dependent);
            }
        }
    }

    private bool IsDependentDue(ManifestDispatchView dependent, List<ManifestDispatchView> views)
    {
        // Find parent manifest in the loaded set (only enabled manifests are loaded)
        var parent = views.FirstOrDefault(v =>
            v.Manifest.Id == dependent.Manifest.DependsOnManifestId
        );
        if (parent is null)
        {
            logger.LogTrace(
                "Skipping dependent manifest {ManifestId} - parent manifest {ParentId} not found or disabled",
                dependent.Manifest.Id,
                dependent.Manifest.DependsOnManifestId
            );
            return false;
        }

        // Guard: if parent has a LastSuccessfulRun timestamp but no successful metadata
        // record to back it up, the timestamp is stale (e.g. metadata was truncated/pruned).
        // Skip the dependent to avoid firing it based on orphaned state.
        if (parent.Manifest.LastSuccessfulRun != null && !parent.HasSuccessfulMetadata)
        {
            logger.LogWarning(
                "Parent manifest {ParentId} (name: {ParentName}) has LastSuccessfulRun={LastRun} but no successful metadata — skipping dependent {DependentId}",
                parent.Manifest.Id,
                parent.Manifest.Name,
                parent.Manifest.LastSuccessfulRun,
                dependent.Manifest.Id
            );
            return false;
        }

        var dependentBaseline = DependentBaseline(dependent);
        if (
            parent.Manifest.LastSuccessfulRun != null
            && (dependentBaseline == null || parent.Manifest.LastSuccessfulRun > dependentBaseline)
        )
        {
            logger.LogDebug(
                "Dependent manifest {ManifestId} (name: {ManifestName}) is due - parent {ParentId} last succeeded at {ParentLastRun}",
                dependent.Manifest.Id,
                dependent.Manifest.Name,
                parent.Manifest.Id,
                parent.Manifest.LastSuccessfulRun
            );
            return true;
        }

        return false;
    }

    private static DateTime? DependentBaseline(ManifestDispatchView dependent)
    {
        // Queue if the parent succeeded after the dependent's latest run started. That is
        // when its latest successful run started, not when it finished, so a parent
        // success that landed while the dependent was running earns it another run (with
        // no run on record, history pruned, its own LastSuccessfulRun stands in); or when
        // its latest cancelled run started, when later, because a cancelled run consumed
        // the parent success it was started for but not one that landed while it ran.
        var successBaseline =
            dependent.LatestSuccessfulRunStart ?? dependent.Manifest.LastSuccessfulRun;
        var dependentBaseline =
            dependent.LatestCancelledRunStart is { } cancelled
            && (successBaseline == null || cancelled > successBaseline)
                ? cancelled
                : successBaseline;

        return dependentBaseline;
    }

    /// <summary>
    /// Checks common guards that apply to all manifest types (dead-lettered, queued, active execution).
    /// </summary>
    private bool ShouldSkipManifest(
        ManifestDispatchView view,
        HashSet<long> newlyDeadLetteredManifestIds
    )
    {
        if (newlyDeadLetteredManifestIds.Contains(view.Manifest.Id))
        {
            logger.LogTrace(
                "Skipping manifest {ManifestId} - was just dead-lettered in this cycle",
                view.Manifest.Id
            );
            return true;
        }

        if (view.HasAwaitingDeadLetter)
        {
            logger.LogTrace(
                "Skipping manifest {ManifestId} - has AwaitingIntervention dead letter",
                view.Manifest.Id
            );
            return true;
        }

        if (view.HasQueuedWork)
        {
            logger.LogTrace(
                "Skipping manifest {ManifestId} - has queued work queue entry",
                view.Manifest.Id
            );
            return true;
        }

        if (view.HasActiveExecution)
        {
            logger.LogTrace(
                "Skipping manifest {ManifestId} - has pending or in-progress execution",
                view.Manifest.Id
            );
            return true;
        }

        return false;
    }
}
