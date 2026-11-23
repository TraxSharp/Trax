using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

internal sealed record ScenarioResult(
    FeatureScenario Scenario,
    ScenarioStatus Status,
    IReadOnlyList<StepResult> Steps
)
{
    /// <summary>The first step that failed or could not be read.</summary>
    internal StepResult? Failure =>
        Steps.FirstOrDefault(s => s.Status is not ScenarioStatus.Passed and not null);

    /// <summary>Steps about the UI, which no driver here checks: <c>--strict</c> fails them.</summary>
    internal IEnumerable<StepResult> Skipped => Steps.Where(s => s.Status is null);
}
