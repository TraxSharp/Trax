using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One outcome of an invoked train, and where it took the instance.</summary>
internal sealed record OutcomeObservation(
    string Machine,
    string State,
    string Phrase,
    string To,
    FeatureScenario Scenario,
    long Line
);
