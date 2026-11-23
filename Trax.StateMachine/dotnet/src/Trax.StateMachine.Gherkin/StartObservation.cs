using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A person starting an instance of their own: <c>Given Ada has an order</c>.</summary>
internal sealed record StartObservation(
    string Machine,
    string Actor,
    FeatureScenario Scenario,
    long Line
);
