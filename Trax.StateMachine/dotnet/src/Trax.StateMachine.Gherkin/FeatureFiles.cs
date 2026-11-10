using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;

namespace Trax.StateMachine.Gherkin;

/// <summary>Given, When or Then, with And and But resolved to the keyword before them.</summary>
internal enum StepKind
{
    Given,
    When,
    Then,
}

/// <summary>One step of a scenario, at the line it is written on.</summary>
internal sealed record FeatureStep(StepKind Kind, string Text, long Line);

/// <summary>
/// One scenario, or one row of an outline, with its Background steps first. It is identified by file and line, never
/// by name: outlines repeat names. An outline row's line is the row's; <see cref="OutlineLine"/> is its header's.
/// </summary>
internal sealed record FeatureScenario(
    string File,
    long Line,
    string Name,
    long? OutlineLine,
    IReadOnlyList<FeatureStep> Steps,
    IReadOnlyList<string> Tags
)
{
    internal string Id => $"{File}:{Line}";

    /// <summary>
    /// The tag of a scenario that needs a handler only the host has (a custom rule's or reduction's C#, an effect's real
    /// receipt): derivation reads its steps and nothing more, an in-memory run skips it, and the host bindings run it.
    /// </summary>
    internal const string HostOnlyTag = "@host";

    /// <summary>Whether the scenario is tagged <see cref="HostOnlyTag"/>.</summary>
    internal bool HostOnly => Tags.Contains(HostOnlyTag, StringComparer.Ordinal);
}

/// <summary>Something derivation cannot accept, at the file and line it is about.</summary>
internal sealed record Problem(string File, long Line, string Message)
{
    /// <summary>The file a problem with the glossary itself names, rather than a feature.</summary>
    internal const string Glossary = "glossary";

    /// <summary>For a problem with the glossary, the JSON pointer of the key it is about, when there is one.</summary>
    internal string? Pointer { get; init; }

    public override string ToString() => $"{File}:{Line}: {Message}";
}

/// <summary>
/// Reads feature files with Cucumber's own parser (ADR 0058) into scenarios: Backgrounds, Rules, outlines and
/// And/But are resolved by the parser. What derivation does not read is refused rather than skipped: a data table, a
/// DocString, and a <c>*</c> step, which has no Given, When or Then meaning.
/// </summary>
internal static class FeatureFiles
{
    internal static (List<FeatureScenario> Scenarios, List<Problem> Problems) Parse(
        IEnumerable<(string Path, string Text)> files
    )
    {
        var scenarios = new List<FeatureScenario>();
        var problems = new List<Problem>();
        foreach (var (path, text) in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            global::Gherkin.Ast.GherkinDocument ast;
            try
            {
                ast = new Parser().Parse(new StringReader(text));
            }
            catch (CompositeParserException ex)
            {
                problems.AddRange(
                    ex.Errors.Select(e => new Problem(path, e.Location?.Line ?? 0, e.Message))
                );
                continue;
            }
            catch (ParserException ex)
            {
                problems.Add(new Problem(path, ex.Location?.Line ?? 0, ex.Message));
                continue;
            }
            if (ast.Feature is null)
                continue;

            var ids = new IncrementingIdGenerator();
            var document = new AstMessagesConverter(ids).ConvertGherkinDocumentToEventArgs(
                ast,
                path
            );
            var lines = Lines(document);
            foreach (var pickle in new PickleCompiler(ids).Compile(document))
            {
                var steps = new List<FeatureStep>();
                foreach (var step in pickle.Steps)
                {
                    var line = lines.GetValueOrDefault(step.AstNodeIds[0]);
                    if (step.Argument?.DataTable is not null)
                        problems.Add(
                            new Problem(
                                path,
                                line,
                                "A data table is not read. Write one step per value."
                            )
                        );
                    if (step.Argument?.DocString is not null)
                        problems.Add(
                            new Problem(
                                path,
                                line,
                                "A DocString is not read. Put the value in quotes in the step."
                            )
                        );
                    if (step.Type is PickleStepType.UNKNOWN)
                    {
                        problems.Add(
                            new Problem(
                                path,
                                line,
                                $"'* {step.Text}' has no Given, When or Then meaning. Write it with one of them."
                            )
                        );
                        continue;
                    }
                    steps.Add(
                        new FeatureStep(
                            step.Type switch
                            {
                                PickleStepType.CONTEXT => StepKind.Given,
                                PickleStepType.ACTION => StepKind.When,
                                _ => StepKind.Then,
                            },
                            step.Text,
                            line
                        )
                    );
                }
                // A pickle's line is its example row's for an outline and its scenario's otherwise. Read from
                // the ids, since the Cucumber.Messages major Reqnroll is built against has no Pickle.Location.
                var scenarioLine = lines.GetValueOrDefault(pickle.AstNodeIds[0]);
                scenarios.Add(
                    new FeatureScenario(
                        path,
                        lines.GetValueOrDefault(pickle.AstNodeIds[^1], scenarioLine),
                        pickle.Name,
                        pickle.AstNodeIds.Count > 1 ? scenarioLine : null,
                        steps,
                        pickle.Tags.Select(t => t.Name).ToList()
                    )
                );
            }
        }
        return (scenarios, problems);
    }

    // The line of every scenario, step and example row, by its id.
    private static Dictionary<string, long> Lines(GherkinDocument document)
    {
        var lines = new Dictionary<string, long>(StringComparer.Ordinal);
        void Steps(IEnumerable<Step> steps)
        {
            foreach (var step in steps)
                lines[step.Id] = step.Location.Line;
        }
        void Scenario(Scenario scenario)
        {
            lines[scenario.Id] = scenario.Location.Line;
            Steps(scenario.Steps);
            foreach (var examples in scenario.Examples)
            foreach (var row in examples.TableBody)
                lines[row.Id] = row.Location.Line;
        }
        foreach (var child in document.Feature.Children)
        {
            if (child.Background is { } background)
                Steps(background.Steps);
            if (child.Scenario is { } scenario)
                Scenario(scenario);
            if (child.Rule is { } rule)
                foreach (var ruleChild in rule.Children)
                {
                    if (ruleChild.Background is { } ruleBackground)
                        Steps(ruleBackground.Steps);
                    if (ruleChild.Scenario is { } ruleScenario)
                        Scenario(ruleScenario);
                }
        }
        return lines;
    }
}
