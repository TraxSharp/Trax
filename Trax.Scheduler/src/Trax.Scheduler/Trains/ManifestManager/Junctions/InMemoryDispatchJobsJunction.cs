using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// InMemory-compatible alternative to <see cref="CreateWorkQueueEntriesJunction"/> that bypasses
/// the work queue and dispatches jobs directly via <see cref="IJobSubmitter"/>.
/// </summary>
/// <remarks>
/// The Postgres pipeline uses a two-phase approach: ManifestManager creates WorkQueue entries,
/// then JobDispatcher claims them using FOR UPDATE SKIP LOCKED. InMemory doesn't support
/// the SQL-based claiming, so this junction creates Metadata and dispatches inline via
/// <see cref="InMemoryJobSubmitter"/>.
///
/// A manifest's stored input type name only selects among the input types of the registered
/// trains; it is never loaded by name. A manifest whose input resolves to none of them is logged
/// and not started, so no run is recorded for it.
/// </remarks>
internal class InMemoryDispatchJobsJunction(
    IDataContext dataContext,
    IJobSubmitter jobSubmitter,
    ITrainRegistry trainRegistry,
    ILogger<InMemoryDispatchJobsJunction> logger
) : EffectJunction<List<ManifestDispatchView>, Unit>
{
    public override async Task<Unit> Run(List<ManifestDispatchView> views)
    {
        var jobsDispatched = 0;

        foreach (var view in views)
        {
            try
            {
                // Resolved before the run is recorded, so a manifest whose input cannot be
                // resolved leaves no Pending row behind.
                var input = view.Manifest is { Properties: not null, PropertyTypeName: not null }
                    ? view.Manifest.GetPropertiesUntyped(trainRegistry.InputTypeToTrain.Keys)
                    : null;

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

                var jobId = input is not null
                    ? await jobSubmitter.EnqueueAsync(metadata.Id, input, CancellationToken)
                    : await jobSubmitter.EnqueueAsync(metadata.Id, CancellationToken);

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
                "InMemoryDispatchJobsJunction completed: {JobsDispatched} jobs dispatched",
                jobsDispatched
            );
        else
            logger.LogDebug("InMemoryDispatchJobsJunction completed: no jobs dispatched");

        return Unit.Default;
    }
}
