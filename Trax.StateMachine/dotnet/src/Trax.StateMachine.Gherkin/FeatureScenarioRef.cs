using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A scenario that produced part of a machine: its file, line and name, never its steps.</summary>
public sealed record FeatureScenarioRef(string File, long Line, string Scenario);
