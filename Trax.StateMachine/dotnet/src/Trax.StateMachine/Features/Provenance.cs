using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>A scenario that produced part of a machine, by file and line; never its steps.</summary>
internal sealed record Provenance(string File, long Line, string Scenario);
