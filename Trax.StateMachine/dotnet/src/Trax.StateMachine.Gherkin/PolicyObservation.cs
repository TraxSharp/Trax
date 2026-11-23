using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// One instance moving because another did: when <see cref="When"/> (owned by <see cref="WhenOwner"/>) entered
/// <see cref="Enters"/>, <see cref="Target"/> went from <see cref="From"/> to <see cref="To"/>. A policy.
/// </summary>
internal sealed record PolicyObservation(
    string When,
    string? WhenOwner,
    string Enters,
    string Target,
    string? TargetOwner,
    string From,
    string To,
    FeatureScenario Scenario,
    long Line
);
