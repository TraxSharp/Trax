namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the local worker pool that dequeues and executes background jobs.
/// </summary>
public class LocalWorkerOptions
{
    /// <summary>
    /// Number of concurrent worker tasks polling for and executing background jobs.
    /// </summary>
    public int WorkerCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// How often idle workers poll for new jobs.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a claimed job stays invisible before another worker can reclaim it.
    /// Provides crash recovery: if a worker dies mid-execution, the job becomes
    /// eligible for re-claim after this timeout.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Number of jobs each worker claims per polling round.
    /// </summary>
    /// <remarks>
    /// Higher values reduce database round-trips when there is a backlog of queued jobs.
    /// Each claimed job is processed sequentially within the worker task. If a worker crashes
    /// mid-batch, uncompleted jobs wait for <see cref="VisibilityTimeout"/> before being reclaimed
    /// by another worker. Default of 1 preserves the original one-job-per-poll behavior.
    /// </remarks>
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// Grace period for in-flight jobs during shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
