using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// The scenario a registered step runs in (<see cref="TraxFeatureDriverOptions.Given"/>): the host, each person as the
/// glossary's steps call it, and a bag the scenario's steps share. A person is one the scenario introduced, by name
/// (<c>Given Ada is a customer</c>); a step never speaks as "I".
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings">SDK reference</seealso>
public sealed class FeatureScenario
{
    private readonly TraxFeatureDriverOptions _options;
    private readonly Glossary _glossary;
    private readonly ScenarioSession _session;
    private readonly HostDriver _driver;

    internal FeatureScenario(
        TraxFeatureDriverOptions options,
        Glossary glossary,
        ScenarioSession session,
        HostDriver driver
    )
    {
        _options = options;
        _glossary = glossary;
        _session = session;
        _driver = driver;
    }

    /// <summary>The host's services, as <see cref="TraxFeatureDriverOptions.Host"/> gives them.</summary>
    public IServiceProvider Services => _options.Host;

    /// <summary>What the scenario's steps hand each other. Empty when the scenario starts, and gone when it ends.</summary>
    public IDictionary<string, object?> Items { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// The credential <paramref name="person"/> calls the host with: the one the glossary's steps send for them, made
    /// once per scenario by <see cref="TraxFeatureDriverOptions.Credential"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The scenario has not introduced <paramref name="person"/>.</exception>
    public FeatureCredential Credential(string person) =>
        _driver.CredentialOf(person, ActorOf(person));

    /// <summary>
    /// Sends <paramref name="request"/> to the host as <paramref name="person"/>, with their credential's headers, on
    /// <see cref="TraxFeatureDriverOptions.Client"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The scenario has not introduced <paramref name="person"/>.</exception>
    public Task<HttpResponseMessage> SendAsync(
        string person,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var (name, value) in Credential(person).Headers)
            request.Headers.TryAddWithoutValidation(name, value);
        return _options.Client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The id of <paramref name="person"/>'s instance of <paramref name="machine"/> (its id in the glossary), or null
    /// when the scenario has not started one for them.
    /// </summary>
    public Guid? InstanceOf(string person, string machine)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(machine);
        return _driver.InstanceOf(new InstanceKey(machine, person));
    }

    private GlossaryActor ActorOf(string person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return _session.People.TryGetValue(person, out var actor)
            ? _glossary.Actors[actor]
            : throw new ArgumentException(
                $"{person} is not a person of this scenario: introduce them first ('Given {person} is {_glossary.Actors.Values.First().Noun}').",
                nameof(person)
            );
    }
}
