using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Runs features on a directory of derived machines, in memory (ADR <c>Trax.Docs/adr/0058</c>). Every scenario gets a
/// fresh driver, so no scenario sees another's instances. A dry run reads every step and runs none.
/// </summary>
internal static class FeatureRun
{
    internal static async Task<RunReport> RunAsync(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features,
        IReadOnlyDictionary<string, JsonObject> irs,
        HostFile host,
        RunSelection selection,
        bool dryRun = false,
        bool failFast = false
    )
    {
        var (scenarios, problems) = FeatureFiles.Parse(features);
        var tags = selection.Tags is { } expression ? TagExpression.Parse(expression) : null;
        var names = selection
            .Names?.Select(n => new Regex(n, RegexOptions.None, TimeSpan.FromSeconds(1)))
            .ToList();
        var resolver = new StepResolver(glossary);
        var results = new List<ScenarioResult>();
        foreach (
            var scenario in scenarios
                .OrderBy(s => s.File, StringComparer.Ordinal)
                .ThenBy(s => s.Line)
        )
        {
            if (tags is not null && !tags(scenario.Tags))
                continue;
            if (names is { Count: > 0 } && !names.Exists(n => n.IsMatch(scenario.Name)))
                continue;
            if (
                selection.Locations is { Count: > 0 } locations
                && !locations.Any(l =>
                    string.Equals(l.File, scenario.File, StringComparison.Ordinal)
                    && (l.Line == scenario.Line || l.Line == scenario.OutlineLine)
                )
            )
                continue;

            if (scenario.HostOnly)
            {
                // It needs a handler only the host has, which no in-memory run loads.
                results.Add(
                    new ScenarioResult(
                        scenario,
                        ScenarioStatus.Skipped,
                        scenario
                            .Steps.Select(s => new StepResult(s, Status: null, "not run"))
                            .ToList()
                    )
                );
                continue;
            }
            var walk = Walk.Run(scenario, glossary, resolver);
            var result = dryRun
                ? DryRun(walk)
                : await ScenarioRunner
                    .Run(walk, glossary, new MemoryDriver(irs, host, glossary))
                    .ConfigureAwait(false);
            results.Add(result);
            if (failFast && result.Status is not ScenarioStatus.Passed)
                break;
        }
        return new RunReport(results, problems);
    }

    // Every step read, none run: an undefined Given or When, or a step the walk refused, still shows.
    private static ScenarioResult DryRun(Walk walk)
    {
        var meanings = walk.Steps.Select(s => s.Step).ToHashSet();
        var steps = walk
            .Scenario.Steps.Select(step =>
                walk.Problems.FirstOrDefault(p => p.Line == step.Line) is { } problem
                    ? new StepResult(step, ScenarioStatus.Undefined, problem.Message)
                : meanings.Contains(step) ? new StepResult(step, Status: null, "not run")
                : step.Kind == StepKind.Then
                    ? new StepResult(step, Status: null, "about the UI: not checked here")
                : new StepResult(
                    step,
                    ScenarioStatus.Undefined,
                    walk.Problems.FirstOrDefault(p => p.Line == step.Line)?.Message
                )
            )
            .ToList();
        return new ScenarioResult(
            walk.Scenario,
            steps.Exists(s => s.Status is ScenarioStatus.Undefined)
                ? ScenarioStatus.Undefined
                : ScenarioStatus.Passed,
            steps
        );
    }
}
