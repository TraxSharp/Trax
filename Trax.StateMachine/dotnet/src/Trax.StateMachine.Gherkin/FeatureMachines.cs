using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// A glossary: the actors, machines, phrases and views a team's features are written in (ADR
/// <c>Trax.Docs/adr/0058</c>). Read one with <see cref="Read"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public sealed class FeatureGlossary
{
    private FeatureGlossary(Glossary glossary, LexiconSource? source)
    {
        Inner = glossary;
        Source = source;
    }

    internal Glossary Inner { get; }

    /// <summary>The lexicon the glossary was elaborated from, or null for one read from its JSON.</summary>
    internal LexiconSource? Source { get; }

    /// <summary>Reads a glossary from its JSON text.</summary>
    /// <exception cref="FormatException">The text is not a glossary; the message names the key.</exception>
    public static FeatureGlossary Read(string json) => new(Glossary.Read(json), null);

    internal static FeatureGlossary From(Glossary glossary, LexiconSource source) =>
        new(glossary, source);

    // A problem derivation found with the glossary rather than a feature, at the lexicon line that wrote the key it is
    // about, when the glossary came from a lexicon.
    internal FeatureProblem Locate(Problem problem) =>
        Source is { } source && problem.File == Problem.Glossary
            ? new FeatureProblem(
                source.Path,
                source.LineOf(problem.Pointer),
                problem.Pointer is null
                    ? problem.Message
                    : source.Message(problem.Pointer, problem.Message)
            )
            : FeatureProblem.Of(problem);
}

/// <summary>One feature file: its path, as provenance and every problem names it, and its text.</summary>
public sealed record FeatureFile(string Path, string Text);

/// <summary>Something a feature says that derivation, or a run, does not accept, at the file and line it is about.</summary>
public sealed record FeatureProblem(string File, long Line, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{File}:{Line}: {Message}";

    internal static FeatureProblem Of(Problem problem) =>
        new(problem.File, problem.Line, problem.Message);
}

/// <summary>
/// What features derive: one IR per machine, by id, the host file and the access report, or the problems that stopped
/// it. A derivation with problems has no files. <see cref="Warnings"/> are designs that derive and may still not hold
/// on the host; they stop nothing.
/// </summary>
public sealed record DerivedMachines(
    IReadOnlyDictionary<string, string> Irs,
    string? Host,
    string? Access,
    IReadOnlyList<FeatureProblem> Problems,
    IReadOnlyList<FeatureProblem> UiSteps
)
{
    /// <summary>Whether the features derived: no problem, and every file written.</summary>
    public bool Succeeded => Problems.Count == 0 && Host is not null;

    /// <summary>
    /// Designs that derive and may still not hold on the host, such as a policy that waits for another policy's result,
    /// at the file and line each is about. They write nothing different and do not change <see cref="Succeeded"/>.
    /// </summary>
    public IReadOnlyList<FeatureProblem> Warnings { get; init; } = [];
}

/// <summary>How a scenario or a step came out.</summary>
public enum FeatureStatus
{
    /// <summary>It ran and every check held.</summary>
    Passed,

    /// <summary>It ran and a check did not hold.</summary>
    Failed,

    /// <summary>A Given or When derivation cannot read: it did not run.</summary>
    Undefined,

    /// <summary>
    /// A step not run: after a failure, in a dry run, or a step about the UI. A scenario tagged <c>@host</c>, which only
    /// the host bindings run, is skipped by an in-memory run.
    /// </summary>
    Skipped,
}

/// <summary>One step's result, and why it failed or was skipped.</summary>
public sealed record FeatureStepResult(
    long Line,
    string Keyword,
    string Text,
    FeatureStatus Status,
    string? Message
);

/// <summary>One scenario's result: where it is, its name and tags, and each step's.</summary>
public sealed record FeatureScenarioResult(
    string File,
    long Line,
    string Name,
    IReadOnlyList<string> Tags,
    FeatureStatus Status,
    IReadOnlyList<FeatureStepResult> Steps
);

/// <summary>A run's results, in file and line order, and what stopped a file from being read.</summary>
public sealed record FeatureRunReport(
    IReadOnlyList<FeatureScenarioResult> Scenarios,
    IReadOnlyList<FeatureProblem> Problems
)
{
    /// <summary>
    /// Whether every file was read and every scenario selected passed, or was skipped as one only the host runs
    /// (<c>@host</c>).
    /// </summary>
    public bool Passed =>
        Problems.Count == 0
        && Scenarios.All(s => s.Status is FeatureStatus.Passed or FeatureStatus.Skipped);
}

/// <summary>
/// Which scenarios a run takes, as Cucumber selects them, and how: a tag expression (<c>@smoke and not @slow</c>),
/// name patterns (any may match), locations (<c>ordering.feature:12</c>), a dry run, and stopping at the first failure.
/// </summary>
public sealed record FeatureRunOptions
{
    /// <summary>A tag expression, or null for every scenario.</summary>
    public string? Tags { get; init; }

    /// <summary>Regular expressions a scenario's name may match, any of them; empty for every name.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Scenarios by file and line (a scenario's, or an example row's); empty for every scenario.</summary>
    public IReadOnlyList<(string File, long Line)> Locations { get; init; } = [];

    /// <summary>Read every step and run none.</summary>
    public bool DryRun { get; init; }

    /// <summary>Stop at the first scenario that does not pass.</summary>
    public bool FailFast { get; init; }
}

/// <summary>
/// Something the glossary names that no scenario shows, and a scenario to paste that shows it, checked green first, or
/// null when none can be written. <see cref="Kind"/> is <c>machine</c>, <c>trigger</c>, <c>state</c>, <c>guard</c>,
/// <c>refusal</c>, <c>outcome</c>, <c>view</c> or <c>grant</c>.
/// </summary>
public sealed record FeatureGap(
    string Kind,
    string Machine,
    string What,
    string Message,
    string? Stub
);

/// <summary>A scenario that produced part of a machine: its file, line and name, never its steps.</summary>
public sealed record FeatureScenarioRef(string File, long Line, string Scenario);

/// <summary>
/// One sticky of the event-storming board: its flow role (<c>actor</c>, <c>command</c>, <c>event</c>, <c>policy</c>,
/// <c>aggregate</c> or <c>view</c>), the machine it belongs to, its label, and the scenarios behind it when the host
/// file has provenance.
/// </summary>
public sealed record FeatureSticky(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<FeatureScenarioRef> Scenarios
);

/// <summary>An arrow on the board, from one sticky's <see cref="FeatureSticky.Id"/> to another's.</summary>
public sealed record FeatureLink(string From, string To);

/// <summary>The event-storming board of a set of machines: its stickies and the arrows between them, sorted.</summary>
public sealed record FeatureBoard(
    IReadOnlyList<FeatureSticky> Stickies,
    IReadOnlyList<FeatureLink> Links
);

/// <summary>
/// Derives state machines from Gherkin features and a glossary, runs features on derived machines in memory, and finds
/// what the features leave out (ADR <c>Trax.Docs/adr/0058</c>). <c>trax machine from-gherkin</c>, <c>run</c> and
/// <c>missing</c> are these calls.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public static class FeatureMachines
{
    /// <summary>
    /// Derives every machine the features use: each machine's IR, the host file that says who may use it, and the access
    /// report, after replaying every scenario on what was derived.
    /// </summary>
    public static DerivedMachines Derive(
        FeatureGlossary glossary,
        IEnumerable<FeatureFile> features
    )
    {
        ArgumentNullException.ThrowIfNull(glossary);
        ArgumentNullException.ThrowIfNull(features);
        var derived = FeatureDerivation.Derive(glossary.Inner, Files(features));
        return new DerivedMachines(
            derived.Irs,
            derived.Host,
            derived.Access,
            derived.Problems.Select(glossary.Locate).ToList(),
            derived.UiSteps.Select(FeatureProblem.Of).ToList()
        )
        {
            Warnings = derived.Warnings.Select(glossary.Locate).ToList(),
        };
    }

    /// <summary>
    /// Runs features in memory on machines: their IRs, by id, and their host file, as <see cref="Derive"/> writes them.
    /// Each answer is the host's, access included.
    /// </summary>
    /// <exception cref="FormatException">
    /// The host file or an IR is not one, or they are not a pair: a machine of one is missing from the other, a name the
    /// host file gives is not in its IR, or the host file was derived from another IR. Or the options' tag expression
    /// is not one.
    /// </exception>
    /// <exception cref="ArgumentException">One of the options' name patterns is not a regular expression.</exception>
    public static FeatureRunReport Run(
        FeatureGlossary glossary,
        IEnumerable<FeatureFile> features,
        IReadOnlyDictionary<string, string> irs,
        string host,
        FeatureRunOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(glossary);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(irs);
        ArgumentNullException.ThrowIfNull(host);
        options ??= new FeatureRunOptions();
        foreach (var name in options.Names)
            try
            {
                _ = new Regex(name, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"The name pattern '{name}' is not a regular expression: {ex.Message}",
                    nameof(options),
                    ex
                );
            }
        var (parsed, hostFile) = Paired(irs, host);
        var report = FeatureRun.Run(
            glossary.Inner,
            Files(features),
            parsed,
            hostFile,
            new RunSelection(options.Tags, options.Names, options.Locations),
            options.DryRun,
            options.FailFast
        );
        return new FeatureRunReport(
            report.Scenarios.Select(Result).ToList(),
            report.Problems.Select(FeatureProblem.Of).ToList()
        );
    }

    /// <summary>As <see cref="Run(FeatureGlossary, IEnumerable{FeatureFile}, IReadOnlyDictionary{string, string}, string, FeatureRunOptions?)"/>, on what <see cref="Derive"/> wrote.</summary>
    /// <exception cref="ArgumentException">The derivation did not succeed, so there is nothing to run on.</exception>
    public static FeatureRunReport Run(
        FeatureGlossary glossary,
        IEnumerable<FeatureFile> features,
        DerivedMachines machines,
        FeatureRunOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(machines);
        if (!machines.Succeeded)
            throw new ArgumentException(
                "The features did not derive, so there are no machines to run them on: fix its problems first.",
                nameof(machines)
            );
        return Run(glossary, features, machines.Irs, machines.Host!, options);
    }

    /// <summary>
    /// What the glossary names and no scenario shows, each with a stub that is green when pasted. Features that do not
    /// derive have no gaps: derive them first.
    /// </summary>
    public static IReadOnlyList<FeatureGap> Missing(
        FeatureGlossary glossary,
        IEnumerable<FeatureFile> features
    )
    {
        ArgumentNullException.ThrowIfNull(glossary);
        ArgumentNullException.ThrowIfNull(features);
        var (gaps, _) = Gherkin.Missing.Find(glossary.Inner, Files(features));
        return gaps.Select(g => new FeatureGap(
                g.Kind.ToString().ToLowerInvariant(),
                g.Machine,
                g.What,
                g.Message,
                g.Stub
            ))
            .ToList();
    }

    /// <summary>
    /// The event-storming board of machines, from their IRs, by id, and their host file alone: who acts, what they ask,
    /// what happens, the rules between, the views, and the scenarios behind each.
    /// </summary>
    /// <exception cref="FormatException">
    /// The host file or an IR is not one, or they are not a pair: a machine of one is missing from the other, a name the
    /// host file gives is not in its IR, or the host file was derived from another IR.
    /// </exception>
    public static FeatureBoard Board(IReadOnlyDictionary<string, string> irs, string host)
    {
        ArgumentNullException.ThrowIfNull(irs);
        ArgumentNullException.ThrowIfNull(host);
        var (parsed, hostFile) = Paired(irs, host);
        var drawn = Features.Board.Draw(parsed, hostFile);
        return new FeatureBoard(
            drawn
                .Stickies.Select(s => new FeatureSticky(
                    s.Id,
                    s.Kind.ToString().ToLowerInvariant(),
                    s.Machine,
                    s.Label,
                    s.Scenarios.Select(p => new FeatureScenarioRef(p.File, p.Line, p.Scenario))
                        .ToList()
                ))
                .ToList(),
            drawn.Links.Select(l => new FeatureLink(l.From, l.To)).ToList()
        );
    }

    // The IRs and the host file, read and checked as a host checks them before it serves them, so a machine with no
    // IR, or an IR from another derivation, is refused here and not partway through a scenario.
    private static (Dictionary<string, JsonObject> Irs, HostFile Host) Paired(
        IReadOnlyDictionary<string, string> irs,
        string host
    )
    {
        var parsed = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (id, text) in irs)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"The IR of '{id}' is not JSON: {ex.Message}", ex);
            }
            parsed[id] =
                node as JsonObject
                ?? throw new FormatException($"The IR of '{id}' is not a JSON object.");
        }
        var hostFile = HostFile.Read(host);
        var problems = MachineFiles.Problems(hostFile, parsed);
        if (problems.Count == 0)
            problems.AddRange(
                parsed
                    .Select(kv => MachineFiles.Mismatch(hostFile, kv.Key, kv.Value))
                    .OfType<string>()
            );
        if (problems.Count > 0)
            throw new FormatException(
                "The machines are not a pair of IRs and their host file:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );
        return (parsed, hostFile);
    }

    private static List<(string Path, string Text)> Files(IEnumerable<FeatureFile> features) =>
        features.Select(f => (f.Path, f.Text)).ToList();

    private static FeatureScenarioResult Result(ScenarioResult result) =>
        new(
            result.Scenario.File,
            result.Scenario.Line,
            result.Scenario.Name,
            result.Scenario.Tags,
            Status(result.Status),
            result
                .Steps.Select(s => new FeatureStepResult(
                    s.Step.Line,
                    s.Step.Kind.ToString(),
                    s.Step.Text,
                    Status(s.Status),
                    s.Message
                ))
                .ToList()
        );

    private static FeatureStatus Status(ScenarioStatus? status) =>
        status switch
        {
            ScenarioStatus.Passed => FeatureStatus.Passed,
            ScenarioStatus.Failed => FeatureStatus.Failed,
            ScenarioStatus.Undefined => FeatureStatus.Undefined,
            _ => FeatureStatus.Skipped,
        };
}
