using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Scheduler.Workflows.ManifestManager;
using Trax.Effect.Services.EffectStep;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Trax.Scheduler.Workflows.ManifestManager.Steps;

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
internal class LoadManifestsStep(IDataContext dataContext)
    : EffectStep<Unit, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(Unit input) =>
        await dataContext
            .Manifests.Where(m => m.IsEnabled)
            .Select(
                m =>
                    new ManifestDispatchView
                    {
                        Manifest = m,
                        ManifestGroup = m.ManifestGroup,
                        FailedCount = m.Metadatas.Count(
                            md => md.WorkflowState == WorkflowState.Failed
                        ),
                        HasAwaitingDeadLetter = m.DeadLetters.Any(
                            dl => dl.Status == DeadLetterStatus.AwaitingIntervention
                        ),
                        HasQueuedWork = m.WorkQueues.Any(q => q.Status == WorkQueueStatus.Queued),
                        HasActiveExecution = m.Metadatas.Any(
                            md =>
                                md.WorkflowState == WorkflowState.Pending
                                || md.WorkflowState == WorkflowState.InProgress
                        ),
                    }
            )
            .AsNoTracking()
            .ToListAsync(CancellationToken);
}
