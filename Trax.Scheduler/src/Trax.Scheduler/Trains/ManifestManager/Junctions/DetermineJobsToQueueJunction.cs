using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
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
internal class DetermineJobsToQueueJunction(
    ILogger<DetermineJobsToQueueJunction> logger,
    SchedulerConfiguration config
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

        var now = DateTime.UtcNow;
        var viewsToQueue = new List<ManifestDispatchView>();

        // Create a set of manifest IDs that were just dead-lettered in this cycle
        var newlyDeadLetteredManifestIds = newlyCreatedDeadLetters
            .Select(dl => dl.ManifestId)
            .ToHashSet();

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
            if (SchedulingHelpers.ShouldRunNow(view.Manifest, now, config, logger))
            {
                logger.LogDebug(
                    "Manifest {ManifestId} (name: {ManifestName}) is due for execution",
                    view.Manifest.Id,
                    view.Manifest.Name
                );
                viewsToQueue.Add(view);
            }
        }

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
                    continue;
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
                    continue;
                }

                // Queue if parent's LastSuccessfulRun is newer than dependent's LastSuccessfulRun
                if (
                    parent.Manifest.LastSuccessfulRun != null
                    && (
                        dependent.Manifest.LastSuccessfulRun == null
                        || parent.Manifest.LastSuccessfulRun > dependent.Manifest.LastSuccessfulRun
                    )
                )
                {
                    logger.LogDebug(
                        "Dependent manifest {ManifestId} (name: {ManifestName}) is due - parent {ParentId} last succeeded at {ParentLastRun}",
                        dependent.Manifest.Id,
                        dependent.Manifest.Name,
                        parent.Manifest.Id,
                        parent.Manifest.LastSuccessfulRun
                    );
                    viewsToQueue.Add(dependent);
                }
            }
        }

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
