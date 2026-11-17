using System.Text.RegularExpressions;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// What the bindings run features against (ADR <c>Trax.Docs/adr/0058</c>): a host in the test process, its services
/// for the Givens and an <see cref="HttpClient"/> to it for everything a client does, the glossary, and a credential
/// per person per scenario, so no scenario shares a user with another.
/// </summary>
public sealed class TraxFeatureDriverOptions
{
    // Unset until the host's configure sets them; reading one that is unset throws, naming it.
    private IServiceProvider? _host;
    private HttpClient? _client;
    private FeatureGlossary? _glossary;
    private Func<FeatureActor, string, string, FeatureCredential>? _credential;

    /// <summary>Whether Host, Client, Glossary and Credential are all set.</summary>
    internal bool HasRequired =>
        _host is not null
        && _client is not null
        && _glossary is not null
        && _credential is not null;

    /// <summary>The host's services: a Given writes through its store, and a step no person takes fires as its server.</summary>
    public IServiceProvider Host
    {
        get => _host.NotNull(nameof(Host));
        set => _host = value;
    }

    /// <summary>A client of the same host, as <c>WebApplicationFactory.CreateClient()</c> gives one.</summary>
    public HttpClient Client
    {
        get => _client.NotNull(nameof(Client));
        set => _client = value;
    }

    /// <summary>The path the host serves GraphQL at. Defaults to <c>/trax/graphql</c>.</summary>
    public string GraphQLPath { get; set; } = "/trax/graphql";

    /// <summary>The glossary the features are written in.</summary>
    public FeatureGlossary Glossary
    {
        get => _glossary.NotNull(nameof(Glossary));
        set => _glossary = value;
    }

    /// <summary>
    /// The credential of a person: their actor, their name in the scenario, and an id unique to the scenario's run, to
    /// make a user of their own from.
    /// </summary>
    public Func<FeatureActor, string, string, FeatureCredential> Credential
    {
        get => _credential.NotNull(nameof(Credential));
        set => _credential = value;
    }

    /// <summary>
    /// How long past its due time a timer, a policy or an invoked train may take on the host before a Then is read
    /// anyway. Defaults to five seconds. Time passing in a scenario is waited for exactly; this bounds only the host's
    /// own lag.
    /// </summary>
    public TimeSpan Slack { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The host's client manifest, as <c>--trax-export-client</c> writes it. When set, every machine operation and view
    /// read is sent by the persisted id the manifest lists for it, as a generated client sends it, so the features run
    /// against a host that requires persisted operations; an operation it lists no document for is not served. When
    /// null, each is sent as a hand-written document, which only a host that accepts inline documents runs. A host that
    /// refuses a request before running it (an inline document it requires persisted, an id of another version) fails
    /// the step, never reads as a denial. Its machines must be the versions the host serves.
    /// </summary>
    public ClientManifest? Manifest { get; set; }

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
        if (
            Steps.Exists(s =>
                s.Kind == kind && string.Equals(s.Pattern, pattern, StringComparison.Ordinal)
            )
        )
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
