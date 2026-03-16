using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Scheduler.Trains.JobDispatcher.Junctions;

/// <summary>
/// Loads all queued work queue entries, ordered by group priority (highest first),
/// then entry priority, then creation time (FIFO).
/// Filters out entries whose ManifestGroup is disabled.
/// </summary>
internal class LoadQueuedJobsJunction(IDataContext dataContext)
    : EffectJunction<Unit, List<WorkQueue>>
{
    public override async Task<List<WorkQueue>> Run(Unit input) =>
        await dataContext
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
