using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>An instance leaving a state once time passed: its state's timer, at most <see cref="Elapsed"/> long.</summary>
internal sealed record TimerObservation(
    string Machine,
    string From,
    TimeSpan Elapsed,
    string To,
    FeatureScenario Scenario,
    long Line
);
