using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.RunOutcomes;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.HostHeartbeatRoles;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.LocalWorkerService;

/// <summary>
/// Background service that runs concurrent worker tasks to dequeue and execute background jobs
/// from the <c>trax.background_job</c> table.
/// </summary>
/// <remarks>
/// Runs on any database provider. On PostgreSQL workers claim with <c>FOR UPDATE SKIP LOCKED</c>,
/// so many workers across processes dequeue without blocking each other. On SQLite a claim runs
/// under <c>BEGIN IMMEDIATE</c>, which takes the file's write lock, so more workers do not help and
/// the pool starts one by default (<see cref="LocalWorkerOptions.WorkerCount"/>). Each worker:
/// 1. Claims up to <see cref="LocalWorkerOptions.BatchSize"/> jobs by setting <c>fetched_at</c> within a transaction
/// 2. Executes each train via <see cref="IJobRunnerTrain"/>
/// 3. Deletes each job row on completion (success or failure)
///
/// Until every job in a batch has finished, its worker refreshes <c>fetched_at</c> on each job it
/// has not finished every third of <see cref="LocalWorkerOptions.VisibilityTimeout"/>, so a long
/// job, and the jobs waiting behind it in the batch, keep their claim.
/// Crash recovery: if a worker dies mid-execution, the <c>fetched_at</c> timestamp stops being
/// refreshed and the job is re-eligible for claim after <see cref="LocalWorkerOptions.VisibilityTimeout"/>.
/// </remarks>
internal sealed class LocalWorkerService(
    IServiceProvider serviceProvider,
    LocalWorkerOptions options,
    ICancellationRegistry cancellationRegistry,
    ILogger<LocalWorkerService> logger,
    ISqlDialect? sqlDialect = null,
    LocalWorkerSlots? slots = null
) : BackgroundService
{
    // The container's clock (AddTrax registers the system's), or the system's when it has none.
    private TimeProvider Clock => serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

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

        await Task.WhenAll(workers).ConfigureAwait(false);

        logger.LogInformation("LocalWorkerService stopping");
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        logger.LogDebug("Worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimedCount = await TryClaimAndExecuteAsync(workerId, stoppingToken)
                    .ConfigureAwait(false);

                if (claimedCount == 0)
                {
                    await Task.Delay(options.PollingInterval, Clock, stoppingToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} encountered an error", workerId);
                await Task.Delay(options.PollingInterval, Clock, stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        logger.LogDebug("Worker {WorkerId} stopped", workerId);
    }

    private async Task<int> TryClaimAndExecuteAsync(int workerId, CancellationToken stoppingToken)
    {
        // Phase 1: Claim jobs atomically
        var claimedJobs = await ClaimJobsAsync(workerId, stoppingToken).ConfigureAwait(false);
        if (claimedJobs.Count == 0)
            return 0;

        // Phase 2 + 3: Execute and clean up each job sequentially. Every job in the batch that
        // has not finished keeps its claim fresh, not only the one running: the jobs waiting
        // behind a long one would otherwise become claimable by another worker once
        // VisibilityTimeout passed.
        var unfinished = new HashSet<long>(claimedJobs.Select(j => j.Id));
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(workerId, unfinished, heartbeatStop.Token);
        try
        {
            for (var i = 0; i < claimedJobs.Count; i++)
            {
                // Once shutdown begins, a job not yet started is released rather than started:
                // each would otherwise get a fresh ShutdownTimeout, so a batch could outlast the
                // host's shutdown window, and a job cut off by the host is only re-claimed after
                // VisibilityTimeout. The heartbeat stops first, so it cannot re-claim a released
                // job by refreshing it afterwards.
                if (stoppingToken.IsCancellationRequested)
                {
                    await heartbeatStop.CancelAsync().ConfigureAwait(false);
                    await StopHeartbeatAsync(workerId, heartbeat).ConfigureAwait(false);
                    await ReleaseUnstartedAsync(workerId, claimedJobs.Skip(i).ToList())
                        .ConfigureAwait(false);
                    break;
                }

                var job = claimedJobs[i];
                using var slot = slots?.Take();
                await ExecuteAndCleanupAsync(
                        workerId,
                        job.Id,
                        job.MetadataId,
                        job.InputJson,
                        job.InputType,
                        stoppingToken
                    )
                    .ConfigureAwait(false);

                lock (unfinished)
                    unfinished.Remove(job.Id);
            }
        }
        finally
        {
            await heartbeatStop.CancelAsync().ConfigureAwait(false);
            await StopHeartbeatAsync(workerId, heartbeat).ConfigureAwait(false);
        }

        return claimedJobs.Count;
    }

    private async Task<List<ClaimedJob>> ClaimJobsAsync(
        int workerId,
        CancellationToken stoppingToken
    )
    {
        var claimScope = serviceProvider.CreateAsyncScope();
        await using (claimScope.ConfigureAwait(false))
        {
            var dataContext = claimScope.ServiceProvider.GetRequiredService<IDataContext>();

            var visibilitySeconds = (int)options.VisibilityTimeout.TotalSeconds;
            var batchSize = Math.Max(1, options.BatchSize);

            using var transaction = await dataContext
                .BeginTransaction(stoppingToken)
                .ConfigureAwait(false);

            var jobs = await dataContext
                .BackgroundJobs.FromSqlRaw(
                    sqlDialect.NotNull().DequeueBackgroundJobs(),
                    visibilitySeconds,
                    batchSize
                )
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            if (jobs.Count == 0)
            {
                await dataContext.RollbackTransaction().ConfigureAwait(false);
                return [];
            }

            // Claim all jobs in the batch
            foreach (var job in jobs)
                job.FetchedAt = Clock.GetUtcNow().UtcDateTime;

            await dataContext.SaveChanges(stoppingToken).ConfigureAwait(false);
            await dataContext.CommitTransaction().ConfigureAwait(false);

            var claimedJobs = jobs.Select(j => new ClaimedJob(
                    j.Id,
                    j.MetadataId,
                    j.Input,
                    j.InputType
                ))
                .ToList();

            logger.LogDebug(
                "Worker {WorkerId} claimed {Count} job(s)",
                workerId,
                claimedJobs.Count
            );

            return claimedJobs;
        }
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
            var releaseScope = serviceProvider.CreateAsyncScope();
            await using var releaseScopeDisposal = releaseScope.ConfigureAwait(false);
            var releaseContext = releaseScope.ServiceProvider.GetRequiredService<IDataContext>();

            await releaseContext
                .BackgroundJobs.Where(j => ids.Contains(j.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.FetchedAt, (DateTime?)null),
                    CancellationToken.None
                )
                .ConfigureAwait(false);

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
            var executeScope = serviceProvider.CreateAsyncScope();
            await using var executeScopeDisposal = executeScope.ConfigureAwait(false);

            var deserializedInput = DeserializeInput(
                executeScope.ServiceProvider,
                jobId,
                inputJson,
                inputType
            );

            var train = executeScope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();

            var request =
                deserializedInput != null
                    ? new RunJobRequest(metadataId, deserializedInput)
                    : new RunJobRequest(metadataId);

            await RunWithShutdownGraceAsync(
                    train,
                    request,
                    workerId,
                    jobId,
                    metadataId,
                    stoppingToken
                )
                .ConfigureAwait(false);
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

            // The job row goes below, so nothing would ever start a run still Pending: fail it
            // here rather than leave it for the stale-pending reaper.
            await FailUnstartedRunAsync(workerId, metadataId).ConfigureAwait(false);
        }

        // Phase 3: Delete the job row (always, on both success and failure).
        // Not cancellable by the stopping token: that token fires when shutdown begins, which is
        // exactly when in-flight jobs finish inside their ShutdownTimeout grace period. The run
        // has already recorded its outcome, so this is bookkeeping for finished work, for the same
        // reason the train's own outcome write is uncancellable (effect/0005). A row left here is
        // re-claimed after VisibilityTimeout and refused as no longer Pending.
        await DeleteJobRowAsync(workerId, jobId).ConfigureAwait(false);
    }

    private async Task RunWithShutdownGraceAsync(
        IJobRunnerTrain train,
        RunJobRequest request,
        int workerId,
        long jobId,
        long metadataId,
        CancellationToken stoppingToken
    )
    {
        // Use shutdown timeout for in-flight jobs: when the host requests shutdown,
        // give the train a grace period before forcefully cancelling.
        // Use an unlinked CTS so we don't cancel immediately — the registration
        // triggers CancelAfter(ShutdownTimeout) to provide a grace period.
        using var shutdownCts = new CancellationTokenSource();
        cancellationRegistry.Register(metadataId, shutdownCts);

        try
        {
            var shutdownRegistration = stoppingToken.Register(() =>
                shutdownCts.CancelAfter(options.ShutdownTimeout)
            );
            await using (shutdownRegistration.ConfigureAwait(false))
            {
                await train.Run(request, shutdownCts.Token).ConfigureAwait(false);

                logger.LogDebug(
                    "Worker {WorkerId} completed job {JobId} (Metadata: {MetadataId})",
                    workerId,
                    jobId,
                    metadataId
                );
            }
        }
        finally
        {
            cancellationRegistry.Unregister(metadataId, shutdownCts);
        }
    }

    private async Task DeleteJobRowAsync(int workerId, long jobId)
    {
        try
        {
            var cleanupScope = serviceProvider.CreateAsyncScope();
            await using var cleanupScopeDisposal = cleanupScope.ConfigureAwait(false);
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<IDataContext>();

            var entity = await cleanupContext
                .BackgroundJobs.FindAsync([jobId], CancellationToken.None)
                .ConfigureAwait(false);
            if (entity != null)
            {
                cleanupContext.BackgroundJobs.Remove(entity);
                await cleanupContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
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

    private static object? DeserializeInput(
        IServiceProvider services,
        long jobId,
        string? inputJson,
        string? inputType
    )
    {
        object? deserializedInput = null;
        if (inputJson != null && inputType != null)
        {
            // Resolved among the registered trains' inputs only; a name that is not one of
            // them fails this job like any other failure, and its row is deleted below.
            var type =
                RegisteredInputTypes.Find(services.GetRequiredService<ITrainRegistry>(), inputType)
                ?? throw new TrainException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Background job {jobId} names input type '{inputType}', which is not "
                    ) + "the input of any registered train."
                );
            deserializedInput = JsonSerializer.Deserialize(
                inputJson,
                type,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        return deserializedInput;
    }

    /// <summary>
    /// Fails run <paramref name="metadataId"/> when its job failed before the run started, and
    /// publishes the failure through the lifecycle hooks, so a machine that invoked it reaches its
    /// <c>OnFailed</c> target now instead of after <see cref="SchedulerConfiguration.StalePendingTimeout"/>.
    /// A run that started records its own outcome and is not Pending, so the write matches nothing.
    /// </summary>
    /// <remarks>
    /// The row records a fixed reason rather than the exception: a lifecycle event reaches
    /// subscribers who are not operators, and the worker's log keeps the detail. Runs on an
    /// uncancellable token for the reason the job row's delete does. A failure here is logged and
    /// leaves the run to the reaper.
    /// </remarks>
    private async Task FailUnstartedRunAsync(int workerId, long metadataId)
    {
        try
        {
            var failScope = serviceProvider.CreateAsyncScope();
            await using var failScopeDisposal = failScope.ConfigureAwait(false);
            var failContext = failScope.ServiceProvider.GetRequiredService<IDataContext>();

            var failed = await ReapedRuns
                .FailAsync(
                    failContext,
                    failContext.Metadatas.Where(m =>
                        m.Id == metadataId && m.TrainState == TrainState.Pending
                    ),
                    [metadataId],
                    "The job failed before its run started. The worker's log has the error.",
                    "JobFailedBeforeRun",
                    nameof(LocalWorkerService),
                    Clock,
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            if (failed.Count > 0)
                await OutOfTrainOutcomes
                    .PublishFailedAsync(serviceProvider, failed, logger)
                    .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Worker {WorkerId} could not fail run {MetadataId}, whose job failed before it started; the stale-pending reaper will",
                workerId,
                metadataId
            );
        }
    }

    // What a PeriodicTimer accepts is one millisecond to about 49.7 days; the upper bound here
    // is the scheduler's own ceiling for a timer, well inside it.
    private static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The heartbeat's interval: a third of <see cref="LocalWorkerOptions.VisibilityTimeout"/>,
    /// held to what a <see cref="PeriodicTimer"/> accepts, or zero (no heartbeat) when the timeout
    /// is not positive.
    /// </summary>
    internal static TimeSpan HeartbeatInterval(TimeSpan visibilityTimeout)
    {
        if (visibilityTimeout <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var interval = visibilityTimeout / 3;
        if (interval < MinHeartbeatInterval)
            return MinHeartbeatInterval;
        if (interval > Operations.SchedulerConfigLimits.MaxTimerInterval)
            return Operations.SchedulerConfigLimits.MaxTimerInterval;
        return interval;
    }

    /// <summary>
    /// Waits for a stopped heartbeat. A heartbeat that ended in an error is logged, never thrown:
    /// the jobs it kept claimed have finished, and its fault is not theirs.
    /// </summary>
    private async Task StopHeartbeatAsync(int workerId, Task heartbeat)
    {
        try
        {
            await heartbeat.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Worker {WorkerId}'s claim refresh ended with an error",
                workerId
            );
        }
    }

    /// <summary>
    /// Refreshes <c>fetched_at</c> on every job in <paramref name="unfinished"/> every third of
    /// <see cref="LocalWorkerOptions.VisibilityTimeout"/> until <paramref name="stop"/> fires. A
    /// claim is only re-claimable once <c>fetched_at</c> is older than the visibility timeout, so
    /// a job this worker has claimed and not finished, running or waiting its turn in the batch,
    /// keeps its claim however long the batch takes, and a worker that dies stops refreshing and
    /// its jobs are recovered as before.
    /// </summary>
    private async Task HeartbeatAsync(
        int workerId,
        HashSet<long> unfinished,
        CancellationToken stop
    )
    {
        var interval = HeartbeatInterval(options.VisibilityTimeout);
        if (interval <= TimeSpan.Zero)
            return;

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                List<long> ids;
                lock (unfinished)
                    ids = unfinished.ToList();

                if (ids.Count == 0)
                    continue;

                try
                {
                    var heartbeatScope = serviceProvider.CreateAsyncScope();
                    await using var heartbeatScopeDisposal = heartbeatScope.ConfigureAwait(false);
                    var heartbeatContext =
                        heartbeatScope.ServiceProvider.GetRequiredService<IDataContext>();
                    var now = Clock.GetUtcNow().UtcDateTime;

                    await heartbeatContext
                        .BackgroundJobs.Where(j => ids.Contains(j.Id))
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(j => j.FetchedAt, (DateTime?)now),
                            stop
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Worker {WorkerId} could not refresh its claim on jobs [{JobIds}]; they "
                            + "become claimable again once VisibilityTimeout passes without a refresh",
                        workerId,
                        string.Join(", ", ids)
                    );
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The batch finished.
        }
    }

    private sealed record ClaimedJob(
        long Id,
        long MetadataId,
        string? InputJson,
        string? InputType
    );
}
