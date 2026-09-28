using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainRegistry;
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
/// 1. Claims up to <see cref="LocalWorkerOptions.BatchSize"/> jobs by setting <c>fetched_at</c> within a transaction
/// 2. Executes each train via <see cref="IJobRunnerTrain"/>
/// 3. Deletes each job row on completion (success or failure)
///
/// Crash recovery: if a worker dies mid-execution, the <c>fetched_at</c> timestamp becomes
/// stale and the job is re-eligible for claim after <see cref="LocalWorkerOptions.VisibilityTimeout"/>.
/// </remarks>
internal class LocalWorkerService(
    IServiceProvider serviceProvider,
    LocalWorkerOptions options,
    ICancellationRegistry cancellationRegistry,
    ILogger<LocalWorkerService> logger,
    ISqlDialect? sqlDialect = null
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "LocalWorkerService starting with {WorkerCount} workers, polling every {PollingInterval}, batch size {BatchSize}",
            options.WorkerCount,
            options.PollingInterval,
            options.BatchSize
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
                var claimedCount = await TryClaimAndExecuteAsync(workerId, stoppingToken);

                if (claimedCount == 0)
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

    private async Task<int> TryClaimAndExecuteAsync(int workerId, CancellationToken stoppingToken)
    {
        // Phase 1: Claim jobs atomically
        List<ClaimedJob> claimedJobs;

        using (var claimScope = serviceProvider.CreateScope())
        {
            var dataContext = claimScope.ServiceProvider.GetRequiredService<IDataContext>();

            var visibilitySeconds = (int)options.VisibilityTimeout.TotalSeconds;
            var batchSize = Math.Max(1, options.BatchSize);

            using var transaction = await dataContext.BeginTransaction(stoppingToken);

            var jobs = await dataContext
                .BackgroundJobs.FromSqlRaw(
                    sqlDialect!.DequeueBackgroundJobs(),
                    visibilitySeconds,
                    batchSize
                )
                .ToListAsync(stoppingToken);

            if (jobs.Count == 0)
            {
                await dataContext.RollbackTransaction();
                return 0;
            }

            // Claim all jobs in the batch
            foreach (var job in jobs)
                job.FetchedAt = DateTime.UtcNow;

            await dataContext.SaveChanges(stoppingToken);
            await dataContext.CommitTransaction();

            claimedJobs = jobs.Select(j => new ClaimedJob(j.Id, j.MetadataId, j.Input, j.InputType))
                .ToList();

            logger.LogDebug(
                "Worker {WorkerId} claimed {Count} job(s)",
                workerId,
                claimedJobs.Count
            );
        }

        // Phase 2 + 3: Execute and clean up each job sequentially
        for (var i = 0; i < claimedJobs.Count; i++)
        {
            // Once shutdown begins, a job not yet started is released rather than started: each
            // would otherwise get a fresh ShutdownTimeout, so a batch could outlast the host's
            // shutdown window, and a job cut off by the host is only re-claimed after
            // VisibilityTimeout.
            if (stoppingToken.IsCancellationRequested)
            {
                await ReleaseUnstartedAsync(workerId, claimedJobs.Skip(i).ToList());
                break;
            }

            var job = claimedJobs[i];
            await ExecuteAndCleanupAsync(
                workerId,
                job.Id,
                job.MetadataId,
                job.InputJson,
                job.InputType,
                stoppingToken
            );
        }

        return claimedJobs.Count;
    }

    /// <summary>
    /// Clears <c>fetched_at</c> on claimed jobs that were never started, so any worker can claim
    /// them at once instead of after <see cref="LocalWorkerOptions.VisibilityTimeout"/>. Runs on an
    /// uncancellable token: it is called only after the stopping token has fired.
    /// </summary>
    private async Task ReleaseUnstartedAsync(int workerId, List<ClaimedJob> unstarted)
    {
        var ids = unstarted.Select(j => j.Id).ToList();

        try
        {
            using var releaseScope = serviceProvider.CreateScope();
            var releaseContext = releaseScope.ServiceProvider.GetRequiredService<IDataContext>();

            await releaseContext
                .BackgroundJobs.Where(j => ids.Contains(j.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.FetchedAt, (DateTime?)null),
                    CancellationToken.None
                );

            logger.LogInformation(
                "Worker {WorkerId} released {Count} claimed job(s) it had not started, because the host is stopping",
                workerId,
                ids.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Worker {WorkerId} failed to release {Count} unstarted job(s); they will be reclaimed after visibility timeout",
                workerId,
                ids.Count
            );
        }
    }

    private async Task ExecuteAndCleanupAsync(
        int workerId,
        long jobId,
        long metadataId,
        string? inputJson,
        string? inputType,
        CancellationToken stoppingToken
    )
    {
        // Phase 2: Execute the train in a fresh scope
        try
        {
            using var executeScope = serviceProvider.CreateScope();

            object? deserializedInput = null;
            if (inputJson != null && inputType != null)
            {
                // Resolved among the registered trains' inputs only; a name that is not one of
                // them fails this job like any other failure, and its row is deleted below.
                var type =
                    RegisteredInputTypes.Find(
                        executeScope.ServiceProvider.GetRequiredService<ITrainRegistry>(),
                        inputType
                    )
                    ?? throw new TrainException(
                        $"Background job {jobId} names input type '{inputType}', which is not "
                            + "the input of any registered train."
                    );
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

        // Phase 3: Delete the job row (always, on both success and failure).
        // Not cancellable by the stopping token: that token fires when shutdown begins, which is
        // exactly when in-flight jobs finish inside their ShutdownTimeout grace period. The run
        // has already recorded its outcome, so this is bookkeeping for finished work, for the same
        // reason the train's own outcome write is uncancellable (effect/0005). A row left here is
        // re-claimed after VisibilityTimeout and refused as no longer Pending.
        try
        {
            using var cleanupScope = serviceProvider.CreateScope();
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<IDataContext>();

            var entity = await cleanupContext.BackgroundJobs.FindAsync(
                jobId,
                CancellationToken.None
            );
            if (entity != null)
            {
                cleanupContext.BackgroundJobs.Remove(entity);
                await cleanupContext.SaveChanges(CancellationToken.None);
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
    }

    private sealed record ClaimedJob(
        long Id,
        long MetadataId,
        string? InputJson,
        string? InputType
    );
}
