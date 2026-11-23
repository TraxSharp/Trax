using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One step's result, and why it failed or was skipped.</summary>
public sealed record FeatureStepResult(
    long Line,
    string Keyword,
    string Text,
    FeatureStatus Status,
    string? Message
);
