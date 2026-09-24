using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.JobDispatcher.Junctions;

/// <summary>
/// Loads queued work queue entries with group-fair batching to prevent starvation.
/// </summary>
/// <remarks>
/// Uses a window function (<c>ROW_NUMBER() OVER (PARTITION BY manifest_group_id)</c>) to ensure
/// every group with queued work is represented in the loaded batch. Without this, a single
/// high-priority group flooding the queue could monopolize the entire batch, starving lower-priority
/// groups even when they have available capacity.
///
/// Manual entries (no manifest) are always included since they have no group association.
/// The loaded entries are ordered by group priority (highest first), then entry priority, then FIFO.
///
/// <see cref="SchedulerConfiguration.MaxQueuedJobsPerCycle"/> controls the per-group batch limit.
/// <see cref="ApplyCapacityLimitsJunction"/> handles the actual global and per-group dispatch caps.
/// </remarks>
internal class LoadQueuedJobsJunction(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ISqlDialect sqlDialect
) : EffectJunction<Unit, List<WorkQueue>>
{
    public override async Task<List<WorkQueue>> Run(Unit input)
    {
        var entries = config.MaxQueuedJobsPerCycle.HasValue
            ? await LoadGroupFair(config.MaxQueuedJobsPerCycle.Value)
            : await LoadAllQueued();

        return FirstPerSubject(entries);
    }

    /// <summary>
    /// Keeps only the first entry for each subject, in dispatch order.
    /// </summary>
    /// <remarks>
    /// Loading drops subjects that already have a run in flight, but not queued siblings of a
    /// subject that is free. Only one of those can be claimed in a cycle, and each of the rest
    /// would still take a capacity slot before the claim refused it, so a burst for one subject
    /// could fill the cycle. The ones left out are loaded again on a later cycle.
    /// </remarks>
    private static List<WorkQueue> FirstPerSubject(List<WorkQueue> entries)
    {
        var subjects = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        return entries.Where(e => e.SubjectKey is null || subjects.Add(e.SubjectKey)).ToList();
    }

    /// <summary>
    /// Loads all queued entries when no batch limit is configured.
    /// </summary>
    private async Task<List<WorkQueue>> LoadAllQueued()
    {
        return await dataContext
            .WorkQueues.AsNoTracking()
            .Include(q => q.Manifest)
                .ThenInclude(m => m!.ManifestGroup)
            .Where(q => q.Status == WorkQueueStatus.Queued)
            // An entry staged by a two-phase enqueue is not dispatchable until promoted. The claim
            // query rejects it anyway; excluding it here keeps it out of the candidate batch so it
            // cannot crowd out work that is actually ready.
            .Where(q => q.ConfirmedAt != null)
            .Where(q => q.ManifestId == null || q.Manifest!.ManifestGroup!.IsEnabled)
            .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DateTime.UtcNow)
            // Subjects with a run still in flight are not candidates. The claim refuses them
            // anyway; dropping them here keeps a blocked subject from crowding the batch.
            .Where(q =>
                q.SubjectKey == null
                || !dataContext.WorkQueues.Any(b =>
                    b.SubjectKey == q.SubjectKey
                    && b.Status == WorkQueueStatus.Dispatched
                    && b.Metadata != null
                    && (
                        b.Metadata.TrainState == TrainState.Pending
                        || b.Metadata.TrainState == TrainState.InProgress
                    )
                )
            )
            .OrderByDescending(q => q.Manifest != null ? q.Manifest.ManifestGroup!.Priority : 0)
            .ThenByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .ToListAsync(CancellationToken);
    }

    /// <summary>
    /// Loads up to <paramref name="perGroupLimit"/> entries per manifest group using a window function,
    /// ensuring every group with queued work is represented in the batch.
    /// Manual entries (no manifest) are always included.
    /// </summary>
    private async Task<List<WorkQueue>> LoadGroupFair(int perGroupLimit)
    {
        // CTE partitions manifest-backed entries by group, keeping only the top N per group.
        // Manual entries (manifest_id IS NULL) are included unconditionally via OR clause.
        //
        // Note: ORDER BY is applied in-memory after loading because EF Core wraps FromSqlRaw
        // in a subquery when .Include() is chained, and Postgres does not guarantee ORDER BY
        // preservation through subqueries.
        var entries = await dataContext
            .WorkQueues.FromSqlRaw(sqlDialect.LoadGroupFairQueuedJobs(), perGroupLimit)
            .AsNoTracking()
            .Include(q => q.Manifest)
                .ThenInclude(m => m!.ManifestGroup)
            .ToListAsync(CancellationToken);

        // Sort in-memory: group priority (desc), entry priority (desc), created_at (asc).
        // This matches the ordering expected by ApplyCapacityLimitsJunction.
        entries.Sort(
            (a, b) =>
            {
                var groupPriorityCmp = GetGroupPriority(b).CompareTo(GetGroupPriority(a));
                if (groupPriorityCmp != 0)
                    return groupPriorityCmp;

                var priorityCmp = b.Priority.CompareTo(a.Priority);
                if (priorityCmp != 0)
                    return priorityCmp;

                return a.CreatedAt.CompareTo(b.CreatedAt);
            }
        );

        return entries;
    }

    private static int GetGroupPriority(WorkQueue entry) =>
        entry.Manifest?.ManifestGroup?.Priority ?? 0;
}
