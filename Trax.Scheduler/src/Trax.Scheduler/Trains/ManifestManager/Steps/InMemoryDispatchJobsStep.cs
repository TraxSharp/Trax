using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectStep;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Trains.ManifestManager.Steps;

/// <summary>
/// InMemory-compatible alternative to <see cref="CreateWorkQueueEntriesStep"/> that bypasses
/// the work queue and dispatches jobs directly via <see cref="IJobSubmitter"/>.
/// </summary>
/// <remarks>
/// The Postgres pipeline uses a two-phase approach: ManifestManager creates WorkQueue entries,
/// then JobDispatcher claims them using FOR UPDATE SKIP LOCKED. InMemory doesn't support
/// the SQL-based claiming, so this step creates Metadata and dispatches inline via
/// <see cref="InMemoryJobSubmitter"/>.
/// </remarks>
internal class InMemoryDispatchJobsStep(
    IDataContext dataContext,
    IJobSubmitter jobSubmitter,
    ILogger<InMemoryDispatchJobsStep> logger
) : EffectStep<List<ManifestDispatchView>, Unit>
{
    public override async Task<Unit> Run(List<ManifestDispatchView> views)
    {
        var jobsDispatched = 0;

        foreach (var view in views)
        {
            try
            {
                var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
                    new CreateMetadata
                    {
                        Name = view.Manifest.Name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                        ManifestId = view.Manifest.Id,
                    }
                );

                await dataContext.Track(metadata);
                await dataContext.SaveChanges(CancellationToken);

                logger.LogDebug(
                    "Created Metadata {MetadataId} for manifest {ManifestId} (name: {ManifestName})",
                    metadata.Id,
                    view.Manifest.Id,
                    view.Manifest.Name
                );

                // Deserialize manifest properties and dispatch inline
                string jobId;
                if (view.Manifest is { Properties: not null, PropertyTypeName: not null })
                {
                    var input = view.Manifest.GetPropertiesUntyped();
                    jobId = await jobSubmitter.EnqueueAsync(metadata.Id, input, CancellationToken);
                }
                else
                {
                    jobId = await jobSubmitter.EnqueueAsync(metadata.Id, CancellationToken);
                }

                logger.LogDebug(
                    "Dispatched manifest {ManifestId} as job {JobId} (Metadata: {MetadataId})",
                    view.Manifest.Id,
                    jobId,
                    metadata.Id
                );

                jobsDispatched++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error dispatching manifest {ManifestId} (name: {ManifestName})",
                    view.Manifest.Id,
                    view.Manifest.Name
                );
            }
        }

        if (jobsDispatched > 0)
            logger.LogInformation(
                "InMemoryDispatchJobsStep completed: {JobsDispatched} jobs dispatched",
                jobsDispatched
            );
        else
            logger.LogDebug("InMemoryDispatchJobsStep completed: no jobs dispatched");

        return Unit.Default;
    }
}
