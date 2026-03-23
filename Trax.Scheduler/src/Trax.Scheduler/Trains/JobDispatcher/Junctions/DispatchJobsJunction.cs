using System.Text.Json;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Trains.JobDispatcher.Junctions;

/// <summary>
/// Creates Metadata records and enqueues each entry to the job submitter.
/// </summary>
/// <remarks>
/// Each entry is dispatched within its own DI scope and database transaction,
/// using <c>FOR UPDATE SKIP LOCKED</c> to atomically claim the work queue entry.
/// This ensures safe concurrent dispatch across multiple server instances.
///
/// When <see cref="SchedulerConfiguration.MaxConcurrentDispatch"/> is greater than 1,
/// entries are dispatched in parallel using a <see cref="SemaphoreSlim"/> to bound concurrency.
/// This is useful for <c>UseRemoteWorkers()</c> where each dispatch blocks on an HTTP POST.
///
/// When <see cref="JobSubmitterRoutingConfiguration"/> is registered, the junction resolves
/// the correct submitter per train based on builder routing or [TraxRemote] attributes.
/// </remarks>
internal class DispatchJobsJunction(
    IServiceProvider serviceProvider,
    ILogger<DispatchJobsJunction> logger,
    JobSubmitterRoutingConfiguration routingConfiguration,
    SchedulerConfiguration schedulerConfiguration
) : EffectJunction<List<WorkQueue>, Unit>
{
    public override async Task<Unit> Run(List<WorkQueue> entries)
    {
        var dispatchStartTime = DateTime.UtcNow;
        var jobsDispatched = 0;

        logger.LogDebug("Starting DispatchJobsJunction for {EntryCount} entries", entries.Count);

        var maxConcurrent = schedulerConfiguration.MaxConcurrentDispatch;

        if (maxConcurrent <= 1)
        {
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
        }
        else
        {
            using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

            var tasks = entries.Select(async entry =>
            {
                await semaphore.WaitAsync(CancellationToken);
                try
                {
                    var dispatched = await TryClaimAndDispatchAsync(entry);

                    if (dispatched)
                        Interlocked.Increment(ref jobsDispatched);
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
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        var duration = DateTime.UtcNow - dispatchStartTime;

        if (jobsDispatched > 0)
            logger.LogInformation(
                "DispatchJobsJunction completed: {JobsDispatched} jobs dispatched in {Duration}ms",
                jobsDispatched,
                duration.TotalMilliseconds
            );
        else
            logger.LogDebug("DispatchJobsJunction completed: no jobs dispatched");

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
            var inputType = TypeResolver.ResolveType(claimed.InputTypeName);
            deserializedInput = JsonSerializer.Deserialize(
                claimed.Input,
                inputType,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        // Create a new Metadata record for this execution.
        // Propagate the WorkQueue's ExternalId so clients can correlate the queue
        // mutation response with subscription events (both use the same externalId).
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new CreateMetadata
            {
                Name = claimed.TrainName,
                ExternalId = claimed.ExternalId,
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

        // Resolve the correct submitter for this train (routed or default)
        var jobSubmitter = ResolveSubmitter(scope.ServiceProvider, claimed.TrainName);

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
                    claimed.Priority,
                    CancellationToken
                );
            else
                backgroundTaskId = await jobSubmitter.EnqueueAsync(
                    metadata.Id,
                    claimed.Priority,
                    CancellationToken
                );

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
                "Failed to enqueue work queue entry {WorkQueueId} (Metadata: {MetadataId}). Handling dispatch failure",
                entry.Id,
                metadata.Id
            );

            await HandleDispatchFailureAsync(entry.Id, metadata.Id, ex);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles a dispatch failure by marking the orphaned Metadata as Failed and optionally
    /// requeuing the work queue entry for retry on the next dispatcher cycle.
    /// </summary>
    /// <remarks>
    /// The Metadata record is always marked as Failed (it represents one failed dispatch attempt).
    /// If <see cref="SchedulerConfiguration.MaxDispatchAttempts"/> is greater than 0 and the
    /// entry hasn't exhausted its attempts, the work queue entry is reset to Queued status
    /// so the next dispatcher cycle creates a new Metadata and retries. The failed Metadata
    /// stays as an immutable audit record.
    /// </remarks>
    private async Task HandleDispatchFailureAsync(
        long workQueueId,
        long metadataId,
        Exception exception
    )
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

            // 1. Mark orphaned metadata as Failed
            var metadata = await dataContext.Metadatas.FirstOrDefaultAsync(
                m => m.Id == metadataId,
                CancellationToken
            );

            if (metadata is not null && metadata.TrainState == TrainState.Pending)
            {
                metadata.TrainState = TrainState.Failed;
                metadata.EndTime = DateTime.UtcNow;
                metadata.AddException(exception);
            }

            // 2. Requeue the work queue entry if attempts remain
            var maxAttempts = schedulerConfiguration.MaxDispatchAttempts;

            if (maxAttempts > 0)
            {
                var workQueueEntry = await dataContext.WorkQueues.FirstOrDefaultAsync(
                    w => w.Id == workQueueId,
                    CancellationToken
                );

                if (workQueueEntry is not null)
                {
                    workQueueEntry.DispatchAttempts++;

                    if (workQueueEntry.DispatchAttempts < maxAttempts)
                    {
                        workQueueEntry.Status = WorkQueueStatus.Queued;
                        workQueueEntry.MetadataId = null;
                        workQueueEntry.DispatchedAt = null;

                        logger.LogWarning(
                            "Requeued work queue entry {WorkQueueId} after dispatch failure "
                                + "(attempt {Attempt}/{MaxAttempts})",
                            workQueueId,
                            workQueueEntry.DispatchAttempts,
                            maxAttempts
                        );
                    }
                    else
                    {
                        logger.LogError(
                            "Work queue entry {WorkQueueId} exhausted dispatch attempts "
                                + "({Attempts}/{MaxAttempts}). Leaving as Dispatched for dead letter handling",
                            workQueueId,
                            workQueueEntry.DispatchAttempts,
                            maxAttempts
                        );
                    }
                }
            }

            await dataContext.SaveChanges(CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to handle dispatch failure for work queue entry {WorkQueueId} "
                    + "(Metadata: {MetadataId}). The ReapStalePendingMetadataJunction will recover it "
                    + "on the next ManifestManager cycle",
                workQueueId,
                metadataId
            );
        }
    }

    /// <summary>
    /// Resolves the appropriate job submitter for a train based on routing configuration.
    /// Falls back to the default IJobSubmitter if no routing is configured for this train.
    /// </summary>
    private IJobSubmitter ResolveSubmitter(IServiceProvider provider, string trainName)
    {
        var concreteType = routingConfiguration.GetSubmitterType(trainName);
        return concreteType is not null
            ? (IJobSubmitter)provider.GetRequiredService(concreteType)
            : provider.GetRequiredService<IJobSubmitter>();
    }
}
