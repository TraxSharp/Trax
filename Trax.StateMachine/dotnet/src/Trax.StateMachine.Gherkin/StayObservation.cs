using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>An instance still in a state after <see cref="Elapsed"/> there: its timer, if any, is longer.</summary>
internal sealed record StayObservation(
    string Machine,
    string State,
    TimeSpan Elapsed,
    FeatureScenario Scenario,
    long Line
);
