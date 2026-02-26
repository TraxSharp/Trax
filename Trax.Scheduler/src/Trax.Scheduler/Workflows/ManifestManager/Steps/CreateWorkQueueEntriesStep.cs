using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Workflows.ManifestManager;
using Trax.Effect.Services.EffectStep;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Workflows.ManifestManager.Steps;

/// <summary>
/// Creates work queue entries for manifests that are due to run.
/// </summary>
/// <remarks>
/// This step replaces the previous EnqueueJobsStep. Instead of directly creating Metadata
/// records and enqueueing to the background task server, it creates WorkQueue entries
/// that will be picked up by the JobDispatcherWorkflow.
/// </remarks>
internal class CreateWorkQueueEntriesStep(
    IDataContext dataContext,
    SchedulerConfiguration schedulerConfiguration,
    ILogger<CreateWorkQueueEntriesStep> logger
) : EffectStep<List<ManifestDispatchView>, Unit>
{
    public override async Task<Unit> Run(List<ManifestDispatchView> views)
    {
        var pollStartTime = DateTime.UtcNow;
        var entriesCreated = 0;

        logger.LogDebug(
            "Starting CreateWorkQueueEntriesStep for {ManifestCount} manifests",
            views.Count
        );

        foreach (var view in views)
        {
            try
            {
                var basePriority = view.ManifestGroup.Priority;
                var effectivePriority =
                    view.Manifest.ScheduleType == ScheduleType.Dependent
                        ? basePriority + schedulerConfiguration.DependentPriorityBoost
                        : basePriority;

                var entry = Models.WorkQueue.WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        WorkflowName = view.Manifest.Name,
                        Input = view.Manifest.Properties,
                        InputTypeName = view.Manifest.PropertyTypeName,
                        ManifestId = view.Manifest.Id,
                        Priority = effectivePriority,
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
            logger.LogInformation(
                "CreateWorkQueueEntriesStep completed: {EntriesCreated} entries created in {Duration}ms",
                entriesCreated,
                duration.TotalMilliseconds
            );
        else
            logger.LogDebug("CreateWorkQueueEntriesStep completed: no entries created");

        return Unit.Default;
    }
}
