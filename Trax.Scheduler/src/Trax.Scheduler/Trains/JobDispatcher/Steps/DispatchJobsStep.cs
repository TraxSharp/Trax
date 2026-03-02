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
using Trax.Scheduler.Services.BackgroundTaskServer;

namespace Trax.Scheduler.Trains.JobDispatcher.Steps;

/// <summary>
/// Creates Metadata records and enqueues each entry to the background task server.
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
    /// creates its Metadata record, and enqueues to the background task server.
    /// </summary>
    /// <returns>True if the entry was successfully dispatched; false if it was already claimed.</returns>
    private async Task<bool> TryClaimAndDispatchAsync(WorkQueue entry)
    {
        using var scope = serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var backgroundTaskServer =
            scope.ServiceProvider.GetRequiredService<IBackgroundTaskServer>();

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
        // updates must be visible to the task server — InMemoryTaskServer executes
        // synchronously and needs to read the Metadata, while PostgresTaskServer and
        // HangfireTaskServer both need the committed state to be visible.
        await dataContext.CommitTransaction();

        // Enqueue to background task server (outside the transaction)
        string backgroundTaskId;
        if (deserializedInput != null)
            backgroundTaskId = await backgroundTaskServer.EnqueueAsync(
                metadata.Id,
                deserializedInput,
                CancellationToken
            );
        else
            backgroundTaskId = await backgroundTaskServer.EnqueueAsync(
                metadata.Id,
                CancellationToken
            );

        logger.LogDebug(
            "Dispatched work queue entry {WorkQueueId} as background task {BackgroundTaskId} (Metadata: {MetadataId})",
            entry.Id,
            backgroundTaskId,
            metadata.Id
        );

        return true;
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
