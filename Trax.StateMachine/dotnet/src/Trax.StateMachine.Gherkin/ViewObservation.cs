using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A person reading a view in a scenario: the view's readers are the actors of the people who read it.</summary>
internal sealed record ViewObservation(
    string View,
    string Actor,
    FeatureScenario Scenario,
    long Line
);
