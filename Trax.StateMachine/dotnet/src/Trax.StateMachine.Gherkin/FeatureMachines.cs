using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

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
        // The public calls are synchronous; every await under this one is ConfigureAwait(false), and the in-memory
        // driver touches no I/O, so the wait holds no context of its own.
        var derived = FeatureDerivation
            .DeriveAsync(glossary.Inner, Files(features))
            .GetAwaiter()
            .GetResult();
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
        // The public calls are synchronous; every await under this one is ConfigureAwait(false), and the in-memory
        // driver touches no I/O, so the wait holds no context of its own.
        var report = FeatureRun
            .RunAsync(
                glossary.Inner,
                Files(features),
                parsed,
                hostFile,
                new RunSelection(options.Tags, options.Names, options.Locations),
                options.DryRun,
                options.FailFast
            )
            .GetAwaiter()
            .GetResult();
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
        // The public calls are synchronous; every await under this one is ConfigureAwait(false), and the in-memory
        // driver touches no I/O, so the wait holds no context of its own.
        var (gaps, _) = Gherkin
            .Missing.FindAsync(glossary.Inner, Files(features))
            .GetAwaiter()
            .GetResult();
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
