using System.Text.RegularExpressions;

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

    internal List<RegisteredStep> Steps { get; } = [];

    /// <summary>
    /// Registers a Given of the test project's own, for a step the glossary does not read. The binding reads every step
    /// with the glossary first and runs a registered step only when no phrase matches, so a registered step never
    /// changes what a glossary step means. <paramref name="pattern"/> is a regular expression matched against the whole
    /// step text; its groups are in the <see cref="Match"/> <paramref name="step"/> is given. A step two registered
    /// patterns match fails as ambiguous.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a regular expression, or is registered twice.</exception>
    public TraxFeatureDriverOptions Given(
        string pattern,
        Func<FeatureScenario, Match, Task> step
    ) => Register(StepKind.Given, pattern, step);

    /// <summary>Registers a When of the test project's own, read as <see cref="Given"/> reads a Given.</summary>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a regular expression, or is registered twice.</exception>
    public TraxFeatureDriverOptions When(string pattern, Func<FeatureScenario, Match, Task> step) =>
        Register(StepKind.When, pattern, step);

    /// <summary>
    /// Registers a Then of the test project's own, read as <see cref="Given"/> reads a Given. It holds unless
    /// <paramref name="step"/> throws.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is not a regular expression, or is registered twice.</exception>
    public TraxFeatureDriverOptions Then(string pattern, Func<FeatureScenario, Match, Task> step) =>
        Register(StepKind.Then, pattern, step);

    private TraxFeatureDriverOptions Register(
        StepKind kind,
        string pattern,
        Func<FeatureScenario, Match, Task> step
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ArgumentNullException.ThrowIfNull(step);
        if (Steps.Any(s => s.Kind == kind && s.Pattern == pattern))
            throw new ArgumentException(
                $"The {kind} '{pattern}' is registered twice.",
                nameof(pattern)
            );
        Steps.Add(
            new RegisteredStep(
                kind,
                pattern,
                new Regex(
                    $"^(?:{pattern})$",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                ),
                step
            )
        );
        return this;
    }
}

/// <summary>A step a test project registered, as <see cref="TraxFeatureDriverOptions.Given"/> took it.</summary>
internal sealed record RegisteredStep(
    StepKind Kind,
    string Pattern,
    Regex Regex,
    Func<FeatureScenario, Match, Task> Run
);

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
