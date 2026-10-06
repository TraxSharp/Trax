namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for dispatching jobs to a remote HTTP endpoint via <c>UseRemoteWorkers()</c>.
/// </summary>
/// <remarks>
/// Set <see cref="SigningKey"/> to sign each request for a runner that verifies signatures, or use
/// <see cref="ConfigureHttpClient"/> to add the credentials a runner's authorization policy expects.
/// </remarks>
public class RemoteWorkerOptions
{
    /// <summary>
    /// The base URL of the remote endpoint that receives job requests.
    /// </summary>
    /// <example>https://my-workers.example.com/trax/execute</example>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure the <see cref="HttpClient"/> used for dispatching jobs.
    /// Use this to add authentication headers, custom timeouts, or any other HTTP configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// remote.ConfigureHttpClient = client =>
    ///     client.DefaultRequestHeaders.Add("Authorization", "Bearer my-token");
    /// </code>
    /// </example>
    public Action<HttpClient>? ConfigureHttpClient { get; set; }

    /// <summary>
    /// HTTP request timeout for each job dispatch.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retry options for transient HTTP failures (429, 502, 503).
    /// </summary>
    /// <remarks>
    /// Defaults to 5 retries with exponential backoff starting at 1 second.
    /// Set <see cref="HttpRetryOptions.MaxRetries"/> to 0 to disable retries.
    /// </remarks>
    public HttpRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// The key shared with the runner's <c>AddTraxJobRunner(runner => runner.SigningKey = ...)</c>,
    /// at least 32 bytes. When set, every request carries a <c>Trax-Signature</c> over its body,
    /// timestamp and nonce, which the runner verifies before it reads the request.
    /// </summary>
    public byte[]? SigningKey { get; set; }
}
