using Reqnroll;

namespace Trax.StateMachine.Gherkin.Reqnroll;

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
    /// feature: a folder on its path is named <c>features</c>, whatever its case, as derivation reads every feature under
    /// it. Every other feature is a spec. A path through both a <c>features</c> and a <c>specs</c> folder is refused:
    /// derivation would read the file as a machine feature, and its author meant a spec.
    /// </summary>
    internal static bool IsMachineFeature(string? folderPath)
    {
        var segments = (folderPath ?? "").Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var features = segments.Any(s => Named(s, "features"));
        if (features && segments.Any(s => Named(s, "specs")))
            throw new InvalidOperationException(
                $"The feature in '{folderPath}' is under both a features folder and a specs folder. Every feature under features/ is a machine feature, derived and replayed; move a spec out to a specs/ folder beside it."
            );
        return features;

        static bool Named(string segment, string name) =>
            string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);
    }

    internal async Task Step(StepKind kind, string text)
    {
        var meaning = _resolver.Resolve(text, _session.People, out var problem, out var unresolved);
        if (meaning is not null)
        {
            // Reqnroll reports the step's own line; the session needs none.
            if (
                await _session
                    .Step(new FeatureStep(kind, text, 0), meaning)
                    .ConfigureAwait(false) is
                { } failure
            )
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
            // A registered When is a When like any other: the Thens after it read only what it set off.
            if (kind == StepKind.When)
                await _session.BeginWhen().ConfigureAwait(false);
            await matches[0].Step.Run(_scenario, matches[0].Match).ConfigureAwait(false);
            return;
        }
        if (kind == StepKind.Then && _machineFeature)
            throw new PendingStepException($"'{text}' is about the UI: no Trax step reads it.");
        throw new InvalidOperationException(
            $"No phrase in the glossary and no registered {kind} says '{text}'."
        );
    }
}
