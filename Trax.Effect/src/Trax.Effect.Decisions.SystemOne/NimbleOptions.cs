namespace Trax.Effect.Decisions.SystemOne;

/// <summary>
/// Where <c>AddNimbleDecider</c> reaches Nimble, Bespoke Labs' open-weights typed decision model,
/// on a server you run with Nimble's own serving code.
/// </summary>
/// <remarks>
/// Nimble's server (<c>nimble/serving/server.py</c> in <c>bespokelabsai/nimble</c>, on
/// <c>openjev-sglang</c>) answers <c>POST /v1/systemone</c> in the System One request format, so
/// this resolves to <see cref="SystemOneOptions"/> with that server's model name and limits filled
/// in.
///
/// <para>There is no default <see cref="Endpoint"/>. Nimble's documentation describes no
/// production hosted API, and the public demo it links to is unauthenticated, runs on one GPU and
/// is not somewhere to send a train's state, so the URL of your own server is required and
/// checked when the host starts.</para>
///
/// <para>The server sets its own limits: it accepts 64 questions a request, 2 to 26 options or
/// levels a question, 8,192 prompt tokens per question and a 2 MiB body, and turns away a fifth
/// concurrent evaluation per container with a 529. Token and body limits cannot be checked here,
/// so a request over them is answered 413 or 422 and fails as permanent.</para>
/// </remarks>
public sealed class NimbleOptions
{
    /// <summary>
    /// The checkpoint id Nimble's server answers to, besides the floating <c>nimble-latest</c>.
    /// </summary>
    /// <remarks>
    /// The request cannot pin a revision: the server serves whichever revision it was deployed
    /// with and echoes back the name it was asked for. Pin the revision where the server is
    /// deployed, and re-tune confidence thresholds when you change it.
    /// </remarks>
    public const string DefaultModel = "bespokelabs/Bespoke-Nimble-9B";

    /// <summary>
    /// How many evaluations one Nimble server container runs at once before it answers 529.
    /// </summary>
    public const int DefaultMaxConcurrentRequests = 4;

    /// <summary>The most options or levels Nimble's server accepts on one question.</summary>
    public const int DefaultMaxOptions = 26;

    /// <summary>The most questions Nimble's server accepts in one request.</summary>
    public const int MaxQuestionsPerRequest = 64;

    /// <summary>
    /// The full URL of <c>POST /v1/systemone</c> on a Nimble server you run, for example
    /// <c>https://nimble.internal.example/v1/systemone</c> or
    /// <c>http://localhost:8000/v1/systemone</c>. Required. Must be HTTPS unless it is a loopback
    /// address.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The server's API key, sent as a bearer token. Nimble's server checks one only when it is
    /// started with <c>OPENJEV_API_KEY</c>; leave this unset, or blank, for one that is not.
    /// Never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model name sent with each request. Defaults to <see cref="DefaultModel"/>; set it when
    /// your server is deployed under another name. <c>nimble-latest</c> is refused.
    /// </summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// How many requests may be in flight at once, or null for no limit. Defaults to
    /// <see cref="DefaultMaxConcurrentRequests"/>, one server container's capacity; raise it when
    /// the server scales to more containers.
    /// </summary>
    public int? MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;

    /// <summary>
    /// The most options or levels one question may offer. Defaults to
    /// <see cref="DefaultMaxOptions"/>; raise it only for a server whose prompt code accepts more.
    /// </summary>
    public int MaxOptions { get; set; } = DefaultMaxOptions;

    /// <summary>
    /// How long one attempt may take. Defaults to 30 seconds, longer than for a hosted model,
    /// because a self-hosted server can be slow on its first request after loading.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a request is tried, counting the first. Defaults to three.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The wait before the first retry, doubling after each. Defaults to half a second.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest wait before a retry; a longer <c>Retry-After</c> ends the retries. Defaults to
    /// thirty seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>What is wrong with these options that only Nimble can say, or nothing.</summary>
    internal IEnumerable<string> Problems()
    {
        if (Endpoint is null)
            yield return "Endpoint is required: the URL of POST /v1/systemone on a Nimble server "
                + "you run (for example http://localhost:8000/v1/systemone). There is no "
                + "hosted Nimble API to default to.";
    }

    internal SystemOneOptions ToSystemOne() =>
        new()
        {
            Endpoint = Endpoint,
            Model = Model,
            ApiKey = ApiKey,
            MaxConcurrentRequests = MaxConcurrentRequests,
            AttemptTimeout = AttemptTimeout,
            MaxAttempts = MaxAttempts,
            RetryDelay = RetryDelay,
            MaxRetryDelay = MaxRetryDelay,
            MaxQuestions = MaxQuestionsPerRequest,
            MaxOptions = MaxOptions,
        };
}
