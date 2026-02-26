using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.BackgroundTaskServer;
using Trax.Effect.Services.EffectStep;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Workflows.ManifestManager.Steps;

/// <summary>
/// Enqueues manifest executions as background tasks.
/// </summary>
/// <remarks>
/// This step creates Metadata records for each manifest due to run and enqueues them
/// to the background task server for execution. Each Metadata creation is persisted
/// immediately to ensure durability before the background task is enqueued.
///
/// The step returns a PollResult summarizing what was enqueued.
/// </remarks>
internal class EnqueueJobsStep(
    IDataContext dataContext,
    IBackgroundTaskServer backgroundTaskServer,
    ILogger<EnqueueJobsStep> logger
) : EffectStep<List<Manifest>, Unit>
{
    public override async Task<Unit> Run(List<Manifest> manifests)
    {
        var pollStartTime = DateTime.UtcNow;
        var jobsEnqueued = 0;

        logger.LogDebug(
            "Starting EnqueueJobsStep to enqueue {ManifestCount} manifests",
            manifests.Count
        );

        foreach (var manifest in manifests)
        {
            try
            {
                // Create a new Metadata record for this execution
                var metadata = Models.Metadata.Metadata.Create(
                    new CreateMetadata
                    {
                        Name = manifest.Name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null, // The input comes from manifest.Properties during execution
                        ManifestId = manifest.Id
                    }
                );

                // Track the new metadata in the data context
                await dataContext.Track(metadata);

                // Persist immediately to ensure durability before enqueueing
                await dataContext.SaveChanges(CancellationToken);

                logger.LogDebug(
                    "Created Metadata {MetadataId} for manifest {ManifestId} (name: {ManifestName})",
                    metadata.Id,
                    manifest.Id,
                    manifest.Name
                );

                // Enqueue the job to the background task server
                var backgroundTaskId = await backgroundTaskServer.EnqueueAsync(
                    metadata.Id,
                    CancellationToken
                );

                logger.LogDebug(
                    "Enqueued manifest {ManifestId} as background task {BackgroundTaskId} (Metadata: {MetadataId})",
                    manifest.Id,
                    backgroundTaskId,
                    metadata.Id
                );

                jobsEnqueued++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error enqueueing manifest {ManifestId} (name: {ManifestName})",
                    manifest.Id,
                    manifest.Name
                );
                // Continue processing other manifests even if one fails
            }
        }

        var pollEndTime = DateTime.UtcNow;
        var duration = pollEndTime - pollStartTime;

        if (jobsEnqueued > 0)
            logger.LogInformation(
                "EnqueueJobsStep completed: {JobsEnqueued} jobs enqueued in {Duration}ms",
                jobsEnqueued,
                duration.TotalMilliseconds
            );
        else
            logger.LogDebug("EnqueueJobsStep completed: no jobs enqueued");

        return Unit.Default;
    }
}
