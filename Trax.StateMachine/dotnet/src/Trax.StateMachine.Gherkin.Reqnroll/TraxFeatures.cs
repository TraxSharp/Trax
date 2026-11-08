namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>An actor of the glossary, as a credential is minted for one: its name, its roles, or any signed-in caller.</summary>
public sealed record FeatureActor(string Name, IReadOnlyList<string> Roles, bool AnyAuthenticated);

/// <summary>How one person of one scenario calls the host: the headers each of their requests carries.</summary>
public sealed record FeatureCredential(IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// What the bindings run features against (ADR <c>Trax.Docs/adr/0058</c>): a host in the test process, its services
/// for the Givens and an <see cref="HttpClient"/> to it for everything a client does, the glossary, and a credential
/// per person per scenario, so no scenario shares a user with another.
/// </summary>
public sealed class TraxFeatureDriverOptions
{
    /// <summary>The host's services: a Given writes through its store, and a step no person takes fires as its server.</summary>
    public IServiceProvider Host { get; set; } = null!;

    /// <summary>A client of the same host, as <c>WebApplicationFactory.CreateClient()</c> gives one.</summary>
    public HttpClient Client { get; set; } = null!;

    /// <summary>The path the host serves GraphQL at. Defaults to <c>/trax/graphql</c>.</summary>
    public string GraphQLPath { get; set; } = "/trax/graphql";

    /// <summary>The glossary the features are written in.</summary>
    public FeatureGlossary Glossary { get; set; } = null!;

    /// <summary>
    /// The credential of a person: their actor, their name in the scenario, and an id unique to the scenario's run, to
    /// make a user of their own from.
    /// </summary>
    public Func<FeatureActor, string, string, FeatureCredential> Credential { get; set; } = null!;

    /// <summary>
    /// How long past its due time a timer, a policy or an invoked train may take on the host before a Then is read
    /// anyway. Defaults to five seconds. Time passing in a scenario is waited for exactly; this bounds only the host's
    /// own lag.
    /// </summary>
    public TimeSpan Slack { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Configures the bindings for a test run. Call <see cref="Configure"/> once, from a <c>[BeforeTestRun]</c> hook, after
/// the host under test has started.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings">SDK reference</seealso>
public static class TraxFeatures
{
    private static TraxFeatureDriverOptions? s_options;

    /// <summary>Sets what the bindings run against.</summary>
    /// <exception cref="ArgumentException">A required option is not set.</exception>
    public static void Configure(Action<TraxFeatureDriverOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TraxFeatureDriverOptions();
        configure(options);
        if (
            options.Host is null
            || options.Client is null
            || options.Glossary is null
            || options.Credential is null
        )
            throw new ArgumentException(
                "TraxFeatures.Configure needs Host, Client, Glossary and Credential set.",
                nameof(configure)
            );
        s_options = options;
    }

    internal static TraxFeatureDriverOptions Options =>
        s_options
        ?? throw new InvalidOperationException(
            "The Trax feature bindings are not configured: call TraxFeatures.Configure(...) from a [BeforeTestRun] hook."
        );
}
