using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.HostHeartbeats;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Scheduler.Services.HostHeartbeatRoles;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the local worker pool that dequeues and executes background jobs.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/configure-local-workers">SDK reference</seealso>
public class LocalWorkerOptions
{
    /// <summary>
    /// The refusal when a host registers two worker pools: <c>AddTraxWorker()</c> beside an
    /// <c>AddScheduler()</c> that already runs local workers.
    /// </summary>
    internal const string RegisteredTwiceMessage =
        "This host registers two local worker pools: AddScheduler() already runs local workers "
        + "(PostgresJobSubmitter), and AddTraxWorker() adds another with its own options, one of "
        + "which would silently replace the other. Remove AddTraxWorker() and set the worker options "
        + "on the scheduler instead: AddScheduler(scheduler => scheduler.ConfigureLocalWorkers(o => ...)).";

    private int? _workerCount;

    /// <summary>
    /// Number of concurrent worker tasks polling for and executing background jobs. Read when the
    /// worker pool starts, so a change made at runtime applies after a restart. Must be between 1
    /// and 256.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="Environment.ProcessorCount"/>, or to 1 on a database that takes one
    /// writer at a time (SQLite), where every claim takes the write lock and more workers only
    /// wait on each other. A value set here wins on either.
    /// </remarks>
    public int WorkerCount
    {
        get => _workerCount ?? (SingleWriter ? 1 : Environment.ProcessorCount);
        set => _workerCount = value;
    }

    /// <summary>
    /// Set when the container resolves the options, from the database's dialect: whether it takes
    /// one writer at a time, which lowers the default <see cref="WorkerCount"/> to 1.
    /// </summary>
    internal bool SingleWriter { get; set; }

    /// <summary>
    /// Registers <paramref name="options"/> so that the container, on resolving them, records
    /// whether the database takes one writer at a time.
    /// </summary>
    internal static void Register(IServiceCollection services, LocalWorkerOptions options)
    {
        services.AddSingleton(sp =>
        {
            options.SingleWriter = sp.GetService<ISqlDialect>()?.HasSingleWriter == true;
            return options;
        });

        // The pool's size and busy workers, reported on this host's heartbeat row.
        services.AddSingleton<LocalWorkerSlots>();
        services.AddSingleton<IHostHeartbeatRole>(sp => sp.GetRequiredService<LocalWorkerSlots>());
    }

    /// <summary>
    /// How often idle workers poll for new jobs. Must be greater than zero and at most 30 days.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a claimed job stays invisible before another worker can reclaim it.
    /// Provides crash recovery: if a worker dies mid-execution, the job becomes
    /// eligible for re-claim after this timeout. A running job's claim is refreshed every third of
    /// it. Must be between one second and ten years.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Number of jobs each worker claims per polling round. Must be at least 1.
    /// </summary>
    /// <remarks>
    /// Higher values reduce database round-trips when there is a backlog of queued jobs.
    /// Each claimed job is processed sequentially within the worker task, and every job in the
    /// batch keeps its claim until the worker finishes it, however long the jobs ahead of it take,
    /// so jobs waiting behind a long one are not taken by another worker. If a worker crashes
    /// mid-batch, uncompleted jobs wait for <see cref="VisibilityTimeout"/> before being reclaimed
    /// by another worker. Default of 1 preserves the original one-job-per-poll behavior.
    /// </remarks>
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// Grace period for in-flight jobs during shutdown. Must be between zero and 30 days.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The options outside the range the worker pool can run with, each named as
    /// <c>{source}: {property}</c> so the refusal says where the value was set.
    /// </summary>
    internal IEnumerable<string> Problems(string source) =>
        new[]
        {
            SchedulerConfigLimits.WorkerCount(WorkerCount, $"{source}: {nameof(WorkerCount)}"),
            SchedulerConfigLimits.ShortInterval(
                PollingInterval,
                $"{source}: {nameof(PollingInterval)}"
            ),
            SchedulerConfigLimits.PositiveDuration(
                VisibilityTimeout,
                $"{source}: {nameof(VisibilityTimeout)}"
            ),
            SchedulerConfigLimits.AtLeastOne(BatchSize, $"{source}: {nameof(BatchSize)}"),
            SchedulerConfigLimits.TimerDelay(
                ShutdownTimeout,
                $"{source}: {nameof(ShutdownTimeout)}"
            ),
        }.OfType<string>();
}
