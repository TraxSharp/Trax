namespace Trax.Scheduler.Lambda.Configuration;

/// <summary>
/// Retry options for transient AWS Lambda invocation failures (throttling, service errors).
/// </summary>
/// <remarks>
/// Applied to <see cref="LambdaWorkerOptions"/> and <see cref="LambdaRunOptions"/>.
/// Retries on AWS status codes 429 (Throttling), 502 (Bad Gateway), 503 (Service Unavailable),
/// and 504 (Gateway Timeout) with exponential backoff and jitter.
/// Set <see cref="MaxRetries"/> to 0 to disable retries.
/// </remarks>
public class LambdaRetryOptions
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
