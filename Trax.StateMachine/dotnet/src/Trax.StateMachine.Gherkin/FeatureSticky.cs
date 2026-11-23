using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// One sticky of the event-storming board: its flow role (<c>actor</c>, <c>command</c>, <see langword="event"/>, <c>policy</c>,
/// <c>aggregate</c> or <c>view</c>), the machine it belongs to, its label, and the scenarios behind it when the host
/// file has provenance.
/// </summary>
public sealed record FeatureSticky(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<FeatureScenarioRef> Scenarios
);
