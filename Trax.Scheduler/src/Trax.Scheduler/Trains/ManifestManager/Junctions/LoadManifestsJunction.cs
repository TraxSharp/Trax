using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Projects all enabled manifests into lightweight <see cref="ManifestDispatchView"/> records
/// with pre-computed aggregate flags (FailedCount, HasAwaitingDeadLetter, etc.).
/// </summary>
/// <remarks>
/// This replaces the previous eager-loading approach that used .Include() on Metadatas,
/// DeadLetters, and WorkQueues. Those collections are unbounded and grow with every
/// scheduled execution, causing increasing memory and query cost over time.
///
/// The projection pushes aggregation into the database via COUNT/EXISTS subqueries,
/// keeping the query cost O(manifests) regardless of child table sizes.
/// </remarks>
internal class LoadManifestsJunction(IDataContext dataContext)
    : EffectJunction<Unit, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(Unit input) =>
        await dataContext
            .Manifests.Where(m => m.IsEnabled)
            .Select(m => new ManifestDispatchView
            {
                Manifest = m,
                ManifestGroup = m.ManifestGroup,
                FailedCount = m.Metadatas.Count(md => md.TrainState == TrainState.Failed),
                HasAwaitingDeadLetter = m.DeadLetters.Any(dl =>
                    dl.Status == DeadLetterStatus.AwaitingIntervention
                ),
                HasQueuedWork = m.WorkQueues.Any(q => q.Status == WorkQueueStatus.Queued),
                HasActiveExecution = m.Metadatas.Any(md =>
                    md.TrainState == TrainState.Pending || md.TrainState == TrainState.InProgress
                ),
            })
            .AsNoTracking()
            .ToListAsync(CancellationToken);
}
