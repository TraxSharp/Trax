using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>One event of a plan: a trigger, and its input when it takes one.</summary>
/// <param name="Trigger">The trigger's name (<c>Next</c>).</param>
/// <param name="Input">The trigger's input, or null when it takes none.</param>
public sealed record PlannedEvent(string Trigger, JsonNode? Input = null);
