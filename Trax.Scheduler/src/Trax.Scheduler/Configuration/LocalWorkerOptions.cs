using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the local worker pool that dequeues and executes background jobs.
/// </summary>
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

    /// <summary>
    /// Number of concurrent worker tasks polling for and executing background jobs. Read when the
    /// worker pool starts, so a change made at runtime applies after a restart. Must be between 1
    /// and 256.
    /// </summary>
    public int WorkerCount { get; set; } = Environment.ProcessorCount;

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
