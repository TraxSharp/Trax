using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

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
