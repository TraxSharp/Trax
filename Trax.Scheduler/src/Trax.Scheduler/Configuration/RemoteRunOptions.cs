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
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/use-remote-run">SDK reference</seealso>
public class RemoteRunOptions
{
    /// <summary>The largest response body read when none is set: 16 MiB.</summary>
    public const long DefaultMaxResponseBodyBytes = 16 * 1024 * 1024;

    /// <summary>
    /// The base URL of the remote endpoint that receives run requests. It must be <c>https://</c>
    /// unless its host is loopback or <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    /// <example>https://my-runner.example.com/trax/run</example>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Accepts an <c>http://</c> <see cref="BaseUrl"/> to a host that is not loopback. Off by
    /// default: the runner's status codes and error bodies are not signed, only a success body is,
    /// so in cleartext an answer could be changed on the way. Set it only when the network to the
    /// runner is trusted, such as a container network on one machine.
    /// </summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>
    /// The largest response body the runner may answer with, in bytes;
    /// <see cref="DefaultMaxResponseBodyBytes"/> (16 MiB) by default. A larger answer fails the
    /// run with <see cref="Services.RunExecutor.RemoteRunException"/> and is not read past the
    /// limit, before its signature is checked. Must be positive.
    /// </summary>
    public long MaxResponseBodyBytes { get; set; } = DefaultMaxResponseBodyBytes;

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
    /// timestamp and nonce, which the runner verifies before it reads the request, and the
    /// runner's answer must carry a <c>Trax-Response-Signature</c> over its body, bound to that
    /// request's nonce; an answer without one, or with one that does not match, fails the run with
    /// <see cref="Services.RunExecutor.RemoteRunException"/>. The response signature authenticates
    /// a success body for that request only: a status code and an error body are not
    /// authenticated, which is why <see cref="BaseUrl"/> must be <c>https://</c> off loopback.
    /// </summary>
    public byte[]? SigningKey { get; set; }
}
