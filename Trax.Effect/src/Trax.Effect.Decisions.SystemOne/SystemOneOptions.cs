namespace Trax.Effect.Decisions.SystemOne;

/// <summary>
/// Where and how <see cref="SystemOneDecider"/> reaches a typed decision model.
/// </summary>
public sealed class SystemOneOptions
{
    /// <summary>
    /// The model's System One endpoint, for example <c>https://api.typesafe.ai/v1/systemone</c>
    /// for Jev, or the URL of a server you run. Required. Must be an <c>http</c> or <c>https</c> URL, and HTTPS unless it is a loopback
    /// address.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The bearer token, or null for an endpoint that needs none. A blank or whitespace key counts
    /// as none, so an unset configuration value sends no <c>Authorization</c> header. Never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model, pinned to a version, for example <c>jev-1.13.0</c>. Required. A confidence
    /// threshold tuned against one version does not carry over to the next, so a floating alias
    /// such as <c>jev-latest</c> is refused unless <see cref="AllowFloatingModel"/> is set.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Accepts a floating model alias. Off by default.</summary>
    public bool AllowFloatingModel { get; set; }

    /// <summary>How long one attempt may take before it is abandoned and retried. Defaults to ten seconds.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many times a request is tried, counting the first, when the model is throttled,
    /// unavailable or slow. Defaults to three.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The wait before the first retry, doubling after each, with jitter, unless the model says
    /// how long to wait. Defaults to half a second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest wait before a retry. The doubling <see cref="RetryDelay"/> stops growing here,
    /// and a model whose <c>Retry-After</c> asks for longer is not retried at all: the decision
    /// fails as transient instead of trying again before the model said it would be ready.
    /// Defaults to thirty seconds; at most a day.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most options one question may offer. The System One format allows 255.</summary>
    public int MaxOptions { get; set; } = 255;

    /// <summary>
    /// The most questions one request may carry. Nimble and OpenJev accept 64, so a
    /// <c>Decide</c> asking more is refused before it is sent rather than answered with a 422.
    /// </summary>
    public int MaxQuestions { get; set; } = 64;

    /// <summary>
    /// How many requests this decider keeps in flight at once, or null for no limit. A limit
    /// makes a busy host queue its decisions instead of being turned away with a 429 or 529.
    /// </summary>
    public int? MaxConcurrentRequests { get; set; }

    private static readonly TimeSpan LongestRetryDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// The settings, checked: what is wrong with them, or nothing.
    /// </summary>
    internal IEnumerable<string> Problems()
    {
        if (Endpoint is null)
            yield return "Endpoint is required.";
        else if (!Endpoint.IsAbsoluteUri)
            yield return $"Endpoint '{Endpoint}' is not an absolute URL.";
        else if (Endpoint.Scheme != Uri.UriSchemeHttp && Endpoint.Scheme != Uri.UriSchemeHttps)
            yield return $"Endpoint '{Endpoint}' is not an http or https URL.";
        else if (Endpoint.Scheme != Uri.UriSchemeHttps && !Endpoint.IsLoopback)
            yield return $"Endpoint '{Endpoint}' is not HTTPS. Only a loopback address may use HTTP, "
                + "because the request carries the train's state and the API key.";

        if (string.IsNullOrWhiteSpace(Model))
            yield return "Model is required, pinned to a version (for example jev-1.13.0).";
        else if (!AllowFloatingModel && IsFloating(Model))
            yield return $"Model '{Model}' is a floating alias. Pin a version, because confidence "
                + "thresholds are tuned against one, or set AllowFloatingModel.";

        if (AttemptTimeout <= TimeSpan.Zero)
            yield return "AttemptTimeout must be positive.";

        if (MaxAttempts < 1)
            yield return "MaxAttempts must be at least 1.";

        if (RetryDelay < TimeSpan.Zero)
            yield return "RetryDelay cannot be negative.";

        if (MaxRetryDelay < TimeSpan.Zero || MaxRetryDelay > LongestRetryDelay)
            yield return "MaxRetryDelay must be between zero and one day.";

        if (MaxOptions is < 2 or > 255)
            yield return "MaxOptions must be between 2 and 255.";

        if (MaxQuestions < 1)
            yield return "MaxQuestions must be at least 1.";

        if (MaxConcurrentRequests is < 1)
            yield return "MaxConcurrentRequests must be at least 1, or null for no limit.";
    }

    /// <summary>The API key to send, or null when it is unset or blank.</summary>
    internal string? Bearer => string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;

    /// <summary>Throws when the options are not usable, naming every problem.</summary>
    /// <exception cref="ArgumentException">The options are not usable.</exception>
    internal void Check(string paramName)
    {
        if (Problems().ToList() is { Count: > 0 } problems)
            throw new ArgumentException(
                $"SystemOneDecider cannot be used: {string.Join(" ", problems)}",
                paramName
            );
    }

    /// <summary>A copy, so a registered decider is not changed by later edits to these options.</summary>
    internal SystemOneOptions Copy() => (SystemOneOptions)MemberwiseClone();

    private static bool IsFloating(string model) =>
        model.EndsWith("latest", StringComparison.OrdinalIgnoreCase) || !model.Any(char.IsDigit);
}
