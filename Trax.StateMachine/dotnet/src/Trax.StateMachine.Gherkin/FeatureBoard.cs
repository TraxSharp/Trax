using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>The event-storming board of a set of machines: its stickies and the arrows between them, sorted.</summary>
public sealed record FeatureBoard(
    IReadOnlyList<FeatureSticky> Stickies,
    IReadOnlyList<FeatureLink> Links
);
