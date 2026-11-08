using Reqnroll;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// The one binding of every step (ADR <c>Trax.Docs/adr/0058</c>): each step is read as derivation reads it and run on
/// the host, so a feature means the same here as in <c>trax machine run</c>. A step the glossary does not read runs the
/// test project's own step for it (<see cref="TraxFeatureDriverOptions.Given"/>), if one is registered. A Given or When
/// neither reads fails. A Then neither reads is about the UI, and is pending in a machine feature, a feature file in a
/// folder named <c>features</c>, as derivation reads them; in any other feature, a spec under <c>specs/</c> among them, it
/// fails, so a mistyped check never passes as pending.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings">SDK reference</seealso>
[Binding]
public sealed class TraxFeatureSteps(ScenarioContext scenario, FeatureContext feature)
{
    private const string SessionKey = "Trax.FeatureSession";

    /// <summary>A Given, an And or But after one.</summary>
    [Given(@"^(.*)$")]
    public Task Given(string text) => Step(StepKind.Given, text);

    /// <summary>A When, an And or But after one.</summary>
    [When(@"^(.*)$")]
    public Task When(string text) => Step(StepKind.When, text);

    /// <summary>A Then, an And or But after one.</summary>
    [Then(@"^(.*)$")]
    public Task Then(string text) => Step(StepKind.Then, text);

    private Task Step(StepKind kind, string text)
    {
        if (!scenario.TryGetValue<HostFeatureRun>(SessionKey, out var run))
        {
            run = new HostFeatureRun(
                TraxFeatures.Options,
                $"{feature.FeatureInfo.Title}/{scenario.ScenarioInfo.Title}/{Guid.NewGuid():N}",
                HostFeatureRun.IsMachineFeature(feature.FeatureInfo.FolderPath)
            );
            scenario.Set(run, SessionKey);
        }
        return run.Step(kind, text);
    }
}

/// <summary>A step whose check did not hold on the host, and why.</summary>
public sealed class FeatureStepFailedException(string message) : Exception(message);

/// <summary>One scenario as the binding runs it on the host: its session, its driver and its registered steps' scenario.</summary>
internal sealed class HostFeatureRun
{
    private readonly TraxFeatureDriverOptions _options;
    private readonly bool _machineFeature;
    private readonly ScenarioSession _session;
    private readonly StepResolver _resolver;
    private readonly FeatureScenario _scenario;

    internal HostFeatureRun(
        TraxFeatureDriverOptions options,
        string scenarioId,
        bool machineFeature
    )
    {
        _options = options;
        _machineFeature = machineFeature;
        var glossary = options.Glossary.Inner;
        var driver = new HostDriver(options, glossary, scenarioId);
        _session = new ScenarioSession(glossary, driver);
        _resolver = new StepResolver(glossary);
        _scenario = new FeatureScenario(options, glossary, _session, driver);
    }

    /// <summary>
    /// Whether a feature in <paramref name="folderPath"/> (Reqnroll's <c>FeatureInfo.FolderPath</c>) is a machine
    /// feature: its folder is named <c>features</c>, whatever its case. Every other feature is a spec.
    /// </summary>
    internal static bool IsMachineFeature(string? folderPath)
    {
        var segments = (folderPath ?? "").Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && string.Equals(segments[^1], "features", StringComparison.OrdinalIgnoreCase);
    }

    internal async Task Step(StepKind kind, string text)
    {
        var meaning = _resolver.Resolve(text, _session.People, out var problem, out var unresolved);
        if (meaning is not null)
        {
            // Reqnroll reports the step's own line; the session needs none.
            if (await _session.Step(new FeatureStep(kind, text, 0), meaning) is { } failure)
                throw new FeatureStepFailedException(failure);
            return;
        }
        if (!unresolved)
            throw new InvalidOperationException(
                problem ?? $"No phrase in the glossary says '{text}'."
            );

        var matches = _options
            .Steps.Where(s => s.Kind == kind)
            .Select(s => (Step: s, Match: s.Regex.Match(text)))
            .Where(m => m.Match.Success)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"'{text}' is ambiguous: the registered {kind}s "
                    + string.Join(", ", matches.Select(m => $"'{m.Step.Pattern}'"))
                    + " all match it."
            );
        if (matches.Count == 1)
        {
            await matches[0].Step.Run(_scenario, matches[0].Match);
            return;
        }
        if (kind == StepKind.Then && _machineFeature)
            throw new PendingStepException($"'{text}' is about the UI: no Trax step reads it.");
        throw new InvalidOperationException(
            $"No phrase in the glossary and no registered {kind} says '{text}'."
        );
    }
}
