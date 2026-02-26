namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the built-in PostgreSQL background task server.
/// </summary>
public class PostgresTaskServerOptions
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
    /// Grace period for in-flight jobs during shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
