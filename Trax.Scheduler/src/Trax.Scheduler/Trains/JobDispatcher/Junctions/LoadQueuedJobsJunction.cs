using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.JobDispatcher.Junctions;

/// <summary>
/// Loads queued work queue entries, ordered by group priority (highest first),
/// then entry priority, then creation time (FIFO).
/// Filters out entries whose ManifestGroup is disabled.
/// Limited by <see cref="SchedulerConfiguration.MaxQueuedJobsPerCycle"/> to prevent
/// unbounded memory usage when the queue is large.
/// </summary>
internal class LoadQueuedJobsJunction(IDataContext dataContext, SchedulerConfiguration config)
    : EffectJunction<Unit, List<WorkQueue>>
{
    public override async Task<List<WorkQueue>> Run(Unit input)
    {
        var query = dataContext
            .WorkQueues.AsNoTracking()
            .Include(q => q.Manifest)
                .ThenInclude(m => m!.ManifestGroup)
            .Where(q => q.Status == WorkQueueStatus.Queued)
            .Where(q => q.ManifestId == null || q.Manifest!.ManifestGroup!.IsEnabled)
            .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DateTime.UtcNow)
            .OrderByDescending(q => q.Manifest != null ? q.Manifest.ManifestGroup!.Priority : 0)
            .ThenByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt);

        if (config.MaxQueuedJobsPerCycle.HasValue)
            return await query
                .Take(config.MaxQueuedJobsPerCycle.Value)
                .ToListAsync(CancellationToken);

        return await query.ToListAsync(CancellationToken);
    }
}
