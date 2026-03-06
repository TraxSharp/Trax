using System.Text.Json;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Trains.JobDispatcher.Steps;

/// <summary>
/// Creates Metadata records and enqueues each entry to the job submitter.
/// </summary>
/// <remarks>
/// Each entry is dispatched within its own DI scope and database transaction,
/// using <c>FOR UPDATE SKIP LOCKED</c> to atomically claim the work queue entry.
/// This ensures safe concurrent dispatch across multiple server instances.
/// </remarks>
internal class DispatchJobsStep(IServiceProvider serviceProvider, ILogger<DispatchJobsStep> logger)
    : EffectStep<List<WorkQueue>, Unit>
{
    public override async Task<Unit> Run(List<WorkQueue> entries)
    {
        var dispatchStartTime = DateTime.UtcNow;
        var jobsDispatched = 0;

        logger.LogDebug("Starting DispatchJobsStep for {EntryCount} entries", entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                var dispatched = await TryClaimAndDispatchAsync(entry);

                if (dispatched)
                    jobsDispatched++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error dispatching work queue entry {WorkQueueId} (train: {TrainName})",
                    entry.Id,
                    entry.TrainName
                );
            }
        }

        var duration = DateTime.UtcNow - dispatchStartTime;

        if (jobsDispatched > 0)
            logger.LogInformation(
                "DispatchJobsStep completed: {JobsDispatched} jobs dispatched in {Duration}ms",
                jobsDispatched,
                duration.TotalMilliseconds
            );
        else
            logger.LogDebug("DispatchJobsStep completed: no jobs dispatched");

        return Unit.Default;
    }

    /// <summary>
    /// Atomically claims a work queue entry using FOR UPDATE SKIP LOCKED,
    /// creates its Metadata record, and enqueues to the job submitter.
    /// </summary>
    /// <returns>True if the entry was successfully dispatched; false if it was already claimed.</returns>
    private async Task<bool> TryClaimAndDispatchAsync(WorkQueue entry)
    {
        using var scope = serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var jobSubmitter = scope.ServiceProvider.GetRequiredService<IJobSubmitter>();

        using var transaction = await dataContext.BeginTransaction(CancellationToken);

        // Atomically claim the entry — skips entries locked by other dispatchers
        var claimed = await dataContext
            .WorkQueues.FromSqlRaw(
                """
                SELECT * FROM trax.work_queue
                WHERE id = {0} AND status = 'queued'
                FOR UPDATE SKIP LOCKED
                """,
                entry.Id
            )
            .FirstOrDefaultAsync(CancellationToken);

        if (claimed is null)
        {
            await dataContext.RollbackTransaction();
            logger.LogDebug(
                "Work queue entry {WorkQueueId} already claimed by another server, skipping",
                entry.Id
            );
            return false;
        }

        // Deserialize input if present
        object? deserializedInput = null;
        if (claimed is { Input: not null, InputTypeName: not null })
        {
            var inputType = ResolveType(claimed.InputTypeName);
            deserializedInput = JsonSerializer.Deserialize(
                claimed.Input,
                inputType,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        // Create a new Metadata record for this execution
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new CreateMetadata
            {
                Name = claimed.TrainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = claimed.ManifestId,
            }
        );

        await dataContext.Track(metadata);
        await dataContext.SaveChanges(CancellationToken);

        // Update work queue entry
        claimed.Status = WorkQueueStatus.Dispatched;
        claimed.MetadataId = metadata.Id;
        claimed.DispatchedAt = DateTime.UtcNow;
        await dataContext.SaveChanges(CancellationToken);

        // Commit the claim transaction before enqueuing. The Metadata and WorkQueue
        // updates must be visible to the job submitter — InMemoryJobSubmitter executes
        // synchronously and needs to read the Metadata, while PostgresJobSubmitter and
        // other submitters need the committed state to be visible.
        await dataContext.CommitTransaction();

        // Enqueue to job submitter (outside the transaction).
        // If this fails, the Metadata is already committed — mark it as Failed
        // immediately so it doesn't stay orphaned in Pending state forever.
        try
        {
            string backgroundTaskId;
            if (deserializedInput != null)
                backgroundTaskId = await jobSubmitter.EnqueueAsync(
                    metadata.Id,
                    deserializedInput,
                    CancellationToken
                );
            else
                backgroundTaskId = await jobSubmitter.EnqueueAsync(metadata.Id, CancellationToken);

            logger.LogDebug(
                "Dispatched work queue entry {WorkQueueId} as background task {BackgroundTaskId} (Metadata: {MetadataId})",
                entry.Id,
                backgroundTaskId,
                metadata.Id
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to enqueue work queue entry {WorkQueueId} (Metadata: {MetadataId}). Marking metadata as failed",
                entry.Id,
                metadata.Id
            );

            await FailOrphanedMetadataAsync(metadata.Id, ex);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Marks an orphaned Metadata record as Failed after an enqueue failure.
    /// Uses a fresh scope since the original transaction was already committed.
    /// </summary>
    private async Task FailOrphanedMetadataAsync(long metadataId, Exception exception)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

            var metadata = await dataContext.Metadatas.FirstOrDefaultAsync(
                m => m.Id == metadataId,
                CancellationToken
            );

            if (metadata is null || metadata.TrainState != TrainState.Pending)
                return;

            metadata.TrainState = TrainState.Failed;
            metadata.EndTime = DateTime.UtcNow;
            metadata.AddException(exception);

            await dataContext.SaveChanges(CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to mark orphaned Metadata {MetadataId} as failed. "
                    + "The ReapStalePendingMetadataStep will recover it on the next ManifestManager cycle",
                metadataId
            );
        }
    }

    private static Type ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null)
                return type;
        }

        throw new TypeLoadException($"Unable to find type: {typeName}");
    }
}
