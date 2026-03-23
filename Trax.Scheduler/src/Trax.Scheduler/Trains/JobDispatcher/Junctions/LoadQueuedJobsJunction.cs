using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
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
internal class LoadQueuedJobsJunction(IDataContext dataContext, SchedulerConfiguration config)
    : EffectJunction<Unit, List<WorkQueue>>
{
    public override async Task<List<WorkQueue>> Run(Unit input)
    {
        if (!config.MaxQueuedJobsPerCycle.HasValue)
            return await LoadAllQueued();

        return await LoadGroupFair(config.MaxQueuedJobsPerCycle.Value);
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
            .Where(q => q.ManifestId == null || q.Manifest!.ManifestGroup!.IsEnabled)
            .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DateTime.UtcNow)
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
        const string sql = """
            WITH ranked AS (
                SELECT wq.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.manifest_group_id
                           ORDER BY wq.priority DESC, wq.created_at ASC
                       ) AS rn
                FROM trax.work_queue wq
                JOIN trax.manifest m ON wq.manifest_id = m.id
                JOIN trax.manifest_group mg ON m.manifest_group_id = mg.id
                WHERE wq.status = 'queued'
                  AND mg.is_enabled = true
                  AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW())
            )
            SELECT wq.* FROM trax.work_queue wq
            WHERE wq.id IN (SELECT ranked.id FROM ranked WHERE ranked.rn <= {0})
               OR (wq.manifest_id IS NULL AND wq.status = 'queued'
                   AND (wq.scheduled_at IS NULL OR wq.scheduled_at <= NOW()))
            """;

        var entries = await dataContext
            .WorkQueues.FromSqlRaw(sql, perGroupLimit)
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
