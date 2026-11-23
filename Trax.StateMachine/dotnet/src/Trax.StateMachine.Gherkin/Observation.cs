using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// One trigger fired in a scenario, the context and input it was fired with, and what it did. <see cref="Arranged"/> is
/// whether a Given put the instance where it was fired from, rather than a move.
/// </summary>
internal sealed record Observation(
    string Machine,
    string From,
    string Trigger,
    JsonObject Context,
    JsonObject? Input,
    string? Person,
    string? Actor,
    Result Result,
    FeatureScenario Scenario,
    long Line,
    bool Arranged = false
);
