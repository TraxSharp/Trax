using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A run: each scenario's result, in file and line order, and what stopped a file from being read.</summary>
internal sealed record RunReport(
    IReadOnlyList<ScenarioResult> Scenarios,
    IReadOnlyList<Problem> Problems
);
