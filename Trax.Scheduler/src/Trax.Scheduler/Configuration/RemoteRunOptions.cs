namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for offloading synchronous run execution to a remote HTTP endpoint via <c>UseRemoteRun()</c>.
/// </summary>
/// <remarks>
/// When configured, <c>run</c> mutations are POSTed to the remote endpoint and block until the
/// train completes and the response is returned. Without this, runs execute in-process (the default).
///
/// Trax does not bake in any authentication mechanism. Use <see cref="ConfigureHttpClient"/>
/// to add authorization headers, API keys, or any custom HTTP configuration your endpoint requires.
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
}
