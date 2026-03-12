using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.LocalWorkerService;

/// <summary>
/// Background service that runs concurrent worker tasks to dequeue and execute background jobs
/// from the <c>trax.background_job</c> table.
/// </summary>
/// <remarks>
/// Workers use PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c> for atomic, lock-free dequeue
/// across multiple workers and processes. Each worker:
/// 1. Claims a job by setting <c>fetched_at</c> within a transaction
/// 2. Executes the train via <see cref="IJobRunnerTrain"/>
/// 3. Deletes the job row on completion (success or failure)
///
/// Crash recovery: if a worker dies mid-execution, the <c>fetched_at</c> timestamp becomes
/// stale and the job is re-eligible for claim after <see cref="LocalWorkerOptions.VisibilityTimeout"/>.
/// </remarks>
internal class LocalWorkerService(
    IServiceProvider serviceProvider,
    LocalWorkerOptions options,
    ICancellationRegistry cancellationRegistry,
    ILogger<LocalWorkerService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "LocalWorkerService starting with {WorkerCount} workers, polling every {PollingInterval}",
            options.WorkerCount,
            options.PollingInterval
        );

        var workers = Enumerable
            .Range(0, options.WorkerCount)
            .Select(i => RunWorkerAsync(i, stoppingToken))
            .ToArray();

        await Task.WhenAll(workers);

        logger.LogInformation("LocalWorkerService stopping");
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        logger.LogDebug("Worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await TryClaimAndExecuteAsync(workerId, stoppingToken);

                if (!claimed)
                {
                    await Task.Delay(options.PollingInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} encountered an error", workerId);
                await Task.Delay(options.PollingInterval, stoppingToken);
            }
        }

        logger.LogDebug("Worker {WorkerId} stopped", workerId);
    }

    private async Task<bool> TryClaimAndExecuteAsync(int workerId, CancellationToken stoppingToken)
    {
        // Phase 1: Claim a job atomically
        long jobId;
        long metadataId;
        string? inputJson;
        string? inputType;

        using (var claimScope = serviceProvider.CreateScope())
        {
            var dataContext = claimScope.ServiceProvider.GetRequiredService<IDataContext>();

            var visibilitySeconds = (int)options.VisibilityTimeout.TotalSeconds;

            using var transaction = await dataContext.BeginTransaction(stoppingToken);

            var job = await dataContext
                .BackgroundJobs.FromSqlRaw(
                    """
                    SELECT * FROM trax.background_job
                    WHERE fetched_at IS NULL
                       OR fetched_at < NOW() - make_interval(secs => {0})
                    ORDER BY created_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """,
                    visibilitySeconds
                )
                .FirstOrDefaultAsync(stoppingToken);

            if (job is null)
            {
                await dataContext.RollbackTransaction();
                return false;
            }

            // Claim the job
            job.FetchedAt = DateTime.UtcNow;
            await dataContext.SaveChanges(stoppingToken);
            await dataContext.CommitTransaction();

            jobId = job.Id;
            metadataId = job.MetadataId;
            inputJson = job.Input;
            inputType = job.InputType;

            logger.LogDebug(
                "Worker {WorkerId} claimed job {JobId} (Metadata: {MetadataId})",
                workerId,
                jobId,
                metadataId
            );
        }

        // Phase 2: Execute the train in a fresh scope
        try
        {
            using var executeScope = serviceProvider.CreateScope();

            object? deserializedInput = null;
            if (inputJson != null && inputType != null)
            {
                var type = TypeResolver.ResolveType(inputType);
                deserializedInput = JsonSerializer.Deserialize(
                    inputJson,
                    type,
                    TraxJsonSerializationOptions.ManifestProperties
                );
            }

            var train = executeScope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();

            var request =
                deserializedInput != null
                    ? new RunJobRequest(metadataId, deserializedInput)
                    : new RunJobRequest(metadataId);

            // Use shutdown timeout for in-flight jobs: when the host requests shutdown,
            // give the train a grace period before forcefully cancelling.
            // Use an unlinked CTS so we don't cancel immediately — the registration
            // triggers CancelAfter(ShutdownTimeout) to provide a grace period.
            using var shutdownCts = new CancellationTokenSource();
            cancellationRegistry.Register(metadataId, shutdownCts);
            try
            {
                await using var shutdownRegistration = stoppingToken.Register(() =>
                    shutdownCts.CancelAfter(options.ShutdownTimeout)
                );

                await train.Run(request, shutdownCts.Token);

                logger.LogDebug(
                    "Worker {WorkerId} completed job {JobId} (Metadata: {MetadataId})",
                    workerId,
                    jobId,
                    metadataId
                );
            }
            finally
            {
                cancellationRegistry.Unregister(metadataId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Worker {WorkerId} failed job {JobId} (Metadata: {MetadataId})",
                workerId,
                jobId,
                metadataId
            );
        }

        // Phase 3: Delete the job row (always, on both success and failure)
        try
        {
            using var cleanupScope = serviceProvider.CreateScope();
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<IDataContext>();

            var entity = await cleanupContext.BackgroundJobs.FindAsync(jobId, stoppingToken);
            if (entity != null)
            {
                cleanupContext.BackgroundJobs.Remove(entity);
                await cleanupContext.SaveChanges(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Worker {WorkerId} failed to delete job {JobId} — it will be reclaimed after visibility timeout",
                workerId,
                jobId
            );
        }

        return true;
    }
}
