namespace Trax.Scheduler.Configuration;

/// <summary>
/// Retry options for transient HTTP failures when dispatching to remote endpoints.
/// </summary>
/// <remarks>
/// Applied to <see cref="RemoteWorkerOptions"/> and <see cref="RemoteRunOptions"/>.
/// Retries on HTTP 429 (Too Many Requests), 502 (Bad Gateway), and 503 (Service Unavailable)
/// with exponential backoff and jitter. Set <see cref="MaxRetries"/> to 0 to disable retries.
/// </remarks>
public class HttpRetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts before giving up.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay between retries. Actual delay is <c>BaseDelay * 2^attempt</c> with jitter,
    /// capped at <see cref="MaxDelay"/>.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries, preventing unbounded exponential growth.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}
