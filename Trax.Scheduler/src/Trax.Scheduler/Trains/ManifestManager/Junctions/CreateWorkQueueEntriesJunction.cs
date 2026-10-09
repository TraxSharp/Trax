using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Trax.Scheduler.Utilities;

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
    RetryDecisionReplay retryReplay,
    ITraxChangeSignal? changeSignal = null,
    RetryResume? retryResume = null
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

        // A retry replays the decisions its failed run recorded, when that is sound, so it takes
        // the tracks the failed run took instead of asking the model again (docs/adr/0017). The
        // sources are read in one pass, from the database, never supplied; a failed lookup asks
        // afresh rather than holding up the cycle.
        var replaySources = await retryReplay.SourcesForRetriesAsync(
            // Exactly the runs the backoff below treats as retries: the latest run failed and
            // its failure still counts. A failure an acknowledged dead letter or the failure
            // window has set aside is not retried, so the next occurrence asks afresh.
            views
                .Where(v => v.LatestFinishedRunFailed && v.FailedCount > 0)
                .Select(v => v.Manifest)
                .ToList(),
            CancellationToken
        );

        // A retry also resumes after its failed run's latest checkpoint, when the train declares one
        // and its chain allows the resume, whatever the manifest says about replaying decisions:
        // declaring a checkpoint is the opt-in (Trax.Docs/adr/0047). Looked up the same way, for
        // the same runs; a failed lookup reruns from the top.
        var resumeSources = retryResume is null
            ? new Dictionary<long, long>()
            : await retryResume.SourcesForRetriesAsync(
                views
                    .Where(v => v.LatestFinishedRunFailed && v.FailedCount > 0)
                    .Select(v => v.Manifest)
                    .ToList(),
                CancellationToken
            );

        // The dispatcher compares ScheduledAt with the database's clock, so a retry's due time is
        // the database's now plus its backoff; read once, by the first retry that has a backoff.
        DateTime? databaseNow = null;

        foreach (var view in views)
        {
            Trax.Effect.Models.WorkQueue.WorkQueue? entry = null;

            try
            {
                // The manifest's own priority, as a trigger or a dead-letter requeue uses. The
                // dispatcher already orders by the group's priority before the entry's.
                var basePriority = view.Manifest.Priority;
                var effectivePriority =
                    view.Manifest.ScheduleType == ScheduleType.Dependent
                        ? basePriority + schedulerConfiguration.DependentPriorityBoost
                        : basePriority;

                // A retry, the run after a failed one, waits out an exponential backoff over the
                // failures in the window. The run after a success or a cancel is an ordinary
                // occurrence and goes on time; its window's failures still count toward the dead
                // letter.
                DateTime? scheduledAt = null;
                if (view.LatestFinishedRunFailed && view.FailedCount > 0)
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
                    // No backoff stores no time, so the retry is due on the next poll whatever the
                    // clocks say. Otherwise its time is the database's (see DatabaseClock): this
                    // host's clock running ahead of the database's would hold it back a poll.
                    if (clampedDelay > TimeSpan.Zero)
                    {
                        var manifestId = view.Manifest.Id;
                        databaseNow ??= await DatabaseClock.UtcNowAsync(
                            dataContext.Manifests.Where(m => m.Id == manifestId),
                            CancellationToken
                        );
                        scheduledAt = databaseNow + clampedDelay;
                    }

                    logger.LogDebug(
                        "Applying retry delay of {Delay} for manifest {ManifestId} (failure #{FailureCount})",
                        clampedDelay,
                        view.Manifest.Id,
                        view.FailedCount
                    );
                }

                entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = view.Manifest.Name,
                        Input = view.Manifest.Properties,
                        InputTypeName = view.Manifest.PropertyTypeName,
                        ManifestId = view.Manifest.Id,
                        Priority = effectivePriority,
                        ScheduledAt = scheduledAt,
                        ReplayDecisionsOf = replaySources.TryGetValue(
                            view.Manifest.Id,
                            out var source
                        )
                            ? source
                            : null,
                        ResumeFrom = resumeSources.TryGetValue(view.Manifest.Id, out var resumed)
                            ? resumed
                            : null,
                    }
                );

                await dataContext.Track(entry);
                // Another host, or a requeue, may have queued a replay of the same run since the
                // lookup: the index refuses a second one and this retry asks afresh instead. An
                // operator's resume of the same run queued since is refused the same way, and this
                // retry reruns from the top.
                await RetryReplayLinks.SaveAskingAfreshOnConflictAsync(
                    dataContext,
                    [entry],
                    logger,
                    CancellationToken
                );

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
                // The failed entry is still tracked as Added, so every later save in this cycle,
                // the reapers' and the dead-letter writes included, would retry it and fail the
                // same way. EF Core rolled the leader transaction back to the savepoint it takes
                // before each save, so detaching the entry is all that is left to undo. A unique
                // violation here is a manifest that a trigger queued after the cycle loaded it.
                if (entry is not null && dataContext is DbContext db)
                    db.Entry(entry).State = EntityState.Detached;

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
