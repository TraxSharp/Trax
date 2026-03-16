using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Enqueues manifest executions as background tasks.
/// </summary>
/// <remarks>
/// This junction creates Metadata records for each manifest due to run and enqueues them
/// to the job submitter for execution. Each Metadata creation is persisted
/// immediately to ensure durability before the background task is enqueued.
///
/// The junction returns a PollResult summarizing what was enqueued.
/// </remarks>
internal class EnqueueJobsJunction(
    IDataContext dataContext,
    IJobSubmitter jobSubmitter,
    ILogger<EnqueueJobsJunction> logger
) : EffectJunction<List<Manifest>, Unit>
{
    public override async Task<Unit> Run(List<Manifest> manifests)
    {
        var pollStartTime = DateTime.UtcNow;
        var jobsEnqueued = 0;

        logger.LogDebug(
            "Starting EnqueueJobsJunction to enqueue {ManifestCount} manifests",
            manifests.Count
        );

        foreach (var manifest in manifests)
        {
            try
            {
                // Create a new Metadata record for this execution
                var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
                    new CreateMetadata
                    {
                        Name = manifest.Name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null, // The input comes from manifest.Properties during execution
                        ManifestId = manifest.Id,
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

                // Enqueue the job to the job submitter
                var backgroundTaskId = await jobSubmitter.EnqueueAsync(
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
                "EnqueueJobsJunction completed: {JobsEnqueued} jobs enqueued in {Duration}ms",
                jobsEnqueued,
                duration.TotalMilliseconds
            );
        else
            logger.LogDebug("EnqueueJobsJunction completed: no jobs enqueued");

        return Unit.Default;
    }
}
