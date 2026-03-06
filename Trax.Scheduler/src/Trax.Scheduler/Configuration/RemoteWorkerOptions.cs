namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for dispatching jobs to a remote HTTP endpoint via <c>UseRemoteWorkers()</c>.
/// </summary>
/// <remarks>
/// Trax does not bake in any authentication mechanism. Use <see cref="ConfigureHttpClient"/>
/// to add authorization headers, API keys, or any custom HTTP configuration your endpoint requires.
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
}
