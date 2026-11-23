using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One scenario's result: where it is, its name and tags, and each step's.</summary>
public sealed record FeatureScenarioResult(
    string File,
    long Line,
    string Name,
    IReadOnlyList<string> Tags,
    FeatureStatus Status,
    IReadOnlyList<FeatureStepResult> Steps
);
