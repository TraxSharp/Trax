using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Creates work queue entries for manifests that are due to run.
/// </summary>
/// <remarks>
/// This junction replaces the previous EnqueueJobsJunction. Instead of directly creating Metadata
/// records and enqueueing to the background task server, it creates WorkQueue entries
/// that will be picked up by the JobDispatcherTrain.
/// </remarks>
internal class CreateWorkQueueEntriesJunction(
    IDataContext dataContext,
    SchedulerConfiguration schedulerConfiguration,
    ILogger<CreateWorkQueueEntriesJunction> logger,
    ITraxChangeSignal? changeSignal = null
) : EffectJunction<List<ManifestDispatchView>, Unit>
{
    public override async Task<Unit> Run(List<ManifestDispatchView> views)
    {
        var pollStartTime = DateTime.UtcNow;
        var entriesCreated = 0;

        logger.LogDebug(
            "Starting CreateWorkQueueEntriesJunction for {ManifestCount} manifests",
            views.Count
        );

        var limit = schedulerConfiguration.MaxWorkQueueEntriesPerCycle;
        if (limit.HasValue && views.Count > limit.Value)
        {
            logger.LogInformation(
                "Applying group-fair batching: {Limit} of {Total} due manifests (excess deferred to next cycle)",
                limit.Value,
                views.Count
            );
            views = SelectGroupFair(views, limit.Value);
        }

        foreach (var view in views)
        {
            try
            {
                var basePriority = view.ManifestGroup.Priority;
                var effectivePriority =
                    view.Manifest.ScheduleType == ScheduleType.Dependent
                        ? basePriority + schedulerConfiguration.DependentPriorityBoost
                        : basePriority;

                // Apply retry delay with exponential backoff when the manifest has prior failures
                DateTime? scheduledAt = null;
                if (view.FailedCount > 0)
                {
                    var delaySeconds =
                        schedulerConfiguration.DefaultRetryDelay.TotalSeconds
                        * Math.Pow(
                            schedulerConfiguration.RetryBackoffMultiplier,
                            view.FailedCount - 1
                        );
                    var clampedDelay = TimeSpan.FromSeconds(
                        Math.Min(delaySeconds, schedulerConfiguration.MaxRetryDelay.TotalSeconds)
                    );
                    scheduledAt = DateTime.UtcNow + clampedDelay;

                    logger.LogDebug(
                        "Applying retry delay of {Delay} for manifest {ManifestId} (failure #{FailureCount})",
                        clampedDelay,
                        view.Manifest.Id,
                        view.FailedCount
                    );
                }

                var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = view.Manifest.Name,
                        Input = view.Manifest.Properties,
                        InputTypeName = view.Manifest.PropertyTypeName,
                        ManifestId = view.Manifest.Id,
                        Priority = effectivePriority,
                        ScheduledAt = scheduledAt,
                    }
                );

                await dataContext.Track(entry);
                await dataContext.SaveChanges(CancellationToken);

                logger.LogDebug(
                    "Created WorkQueue entry {WorkQueueId} for manifest {ManifestId} (name: {ManifestName})",
                    entry.Id,
                    view.Manifest.Id,
                    view.Manifest.Name
                );

                entriesCreated++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error creating work queue entry for manifest {ManifestId} (name: {ManifestName})",
                    view.Manifest.Id,
                    view.Manifest.Name
                );
            }
        }

        var duration = DateTime.UtcNow - pollStartTime;

        if (entriesCreated > 0)
        {
            logger.LogInformation(
                "CreateWorkQueueEntriesJunction completed: {EntriesCreated} entries created in {Duration}ms",
                entriesCreated,
                duration.TotalMilliseconds
            );
            changeSignal?.Notify(ChangeDomain.WorkQueue);
        }
        else
            logger.LogDebug("CreateWorkQueueEntriesJunction completed: no entries created");

        return Unit.Default;
    }

    /// <summary>
    /// Distributes the total limit fairly across manifest groups, ensuring every group
    /// with due manifests gets representation. Each group gets a base allocation of
    /// <c>limit / numGroups</c>, with remainder and unused slots going to higher-priority
    /// groups first. This prevents a single large group from monopolizing the batch and
    /// starving smaller groups.
    /// </summary>
    private List<ManifestDispatchView> SelectGroupFair(List<ManifestDispatchView> views, int limit)
    {
        var groups = views
            .GroupBy(v => v.ManifestGroup.Id)
            .OrderByDescending(g => g.First().ManifestGroup.Priority)
            .Select(g => g.ToList())
            .ToList();

        var numGroups = groups.Count;
        var perGroupBase = limit / numGroups;
        var result = new List<ManifestDispatchView>(limit);

        // First pass: give each group its base allocation
        var leftover = 0;
        var groupTaken = new int[numGroups];
        for (var i = 0; i < numGroups; i++)
        {
            var take = Math.Min(perGroupBase, groups[i].Count);
            groupTaken[i] = take;
            result.AddRange(groups[i].Take(take));
            leftover += perGroupBase - take;
        }

        // Second pass: distribute remainder + leftover to groups that still have capacity,
        // ordered by priority (highest first)
        var extra = (limit - perGroupBase * numGroups) + leftover;
        for (var i = 0; i < numGroups && extra > 0; i++)
        {
            var available = groups[i].Count - groupTaken[i];
            var take = Math.Min(available, extra);
            if (take > 0)
            {
                result.AddRange(groups[i].Skip(groupTaken[i]).Take(take));
                extra -= take;
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            var distribution = result
                .GroupBy(v => v.ManifestGroup.Id)
                .Select(g => $"{g.First().ManifestGroup.Name ?? g.Key.ToString()}={g.Count()}");
            logger.LogDebug(
                "Group-fair distribution: {Distribution}",
                string.Join(", ", distribution)
            );
        }

        return result;
    }
}
