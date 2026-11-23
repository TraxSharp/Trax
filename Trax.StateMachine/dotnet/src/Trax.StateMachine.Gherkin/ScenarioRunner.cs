using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Runs a scenario's steps against a driver: a Given arranges, a When asks, and a Then checks what the driver answers.
/// It stops at the first failing step. The derivation's own check is this runner over the machines it derived.
/// </summary>
internal static class ScenarioRunner
{
    internal static async ValueTask<ScenarioResult> Run(
        Walk walk,
        Glossary glossary,
        IScenarioDriver driver,
        bool failFast = true
    )
    {
        var scenario = walk.Scenario;
        var meanings = walk.Steps.ToDictionary(s => s.Step, s => s.Meaning);
        var results = new List<StepResult>();

        // A Given or When with no meaning, or any step the walk refused, means the scenario does not read: it is not
        // run, as derivation would not derive from it.
        var undefined =
            scenario.Steps.FirstOrDefault(s =>
                (s.Kind != StepKind.Then && !meanings.ContainsKey(s))
                || walk.Problems.Exists(p => p.Line == s.Line)
            ) ?? (walk.Problems.Count > 0 ? scenario.Steps.FirstOrDefault() : null);
        if (undefined is not null)
        {
            var why =
                walk.Problems.FirstOrDefault(p => p.Line == undefined.Line)?.Message
                ?? walk.Problems.FirstOrDefault()?.Message;
            foreach (var step in scenario.Steps)
                results.Add(
                    step == undefined
                        ? new StepResult(step, ScenarioStatus.Undefined, why)
                        : new StepResult(step, Status: null, "not run")
                );
            return new ScenarioResult(scenario, ScenarioStatus.Undefined, results);
        }

        var session = new ScenarioSession(glossary, driver);
        var failed = false;

        foreach (var step in scenario.Steps)
        {
            if (failed && failFast)
            {
                results.Add(new StepResult(step, Status: null, "not run"));
                continue;
            }
            if (!meanings.TryGetValue(step, out var meaning))
            {
                results.Add(new StepResult(step, Status: null, "about the UI: not checked here"));
                continue;
            }
            var failure = await session.Step(step, meaning).ConfigureAwait(false);
            results.Add(
                new StepResult(
                    step,
                    failure is null ? ScenarioStatus.Passed : ScenarioStatus.Failed,
                    failure
                )
            );
            failed |= failure is not null;
        }
        return new ScenarioResult(
            scenario,
            failed ? ScenarioStatus.Failed : ScenarioStatus.Passed,
            results
        );
    }

    internal static string Describe(Answer? answer) =>
        answer switch
        {
            Answer.Moved m => $"taken, moving it to {m.State}",
            Answer.Refused r => $"{r.Code.ToWire()} \"{r.Message}\"",
            Answer.NotServed n => $"not served ({n.What})",
            _ => "nothing",
        };
}
