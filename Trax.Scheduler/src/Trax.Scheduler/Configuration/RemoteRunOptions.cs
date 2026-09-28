namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for offloading synchronous run execution to a remote HTTP endpoint via <c>UseRemoteRun()</c>.
/// </summary>
/// <remarks>
/// When configured, <c>run</c> mutations are POSTed to the remote endpoint and block until the
/// train completes and the response is returned. Without this, runs execute in-process (the default).
///
/// Set <see cref="SigningKey"/> to sign each request for a runner that verifies signatures, or use
/// <see cref="ConfigureHttpClient"/> to add the credentials a runner's authorization policy expects.
/// </remarks>
public class RemoteRunOptions
{
    /// <summary>
    /// The base URL of the remote endpoint that receives run requests.
    /// </summary>
    /// <example>https://my-runner.example.com/trax/run</example>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure the <see cref="HttpClient"/> used for dispatching run requests.
    /// Use this to add authentication headers, custom timeouts, or any other HTTP configuration.
    /// </summary>
    public Action<HttpClient>? ConfigureHttpClient { get; set; }

    /// <summary>
    /// HTTP request timeout for each run dispatch. Defaults to 5 minutes since run requests
    /// block until the train completes (unlike queue dispatch which is fire-and-forget).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

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
