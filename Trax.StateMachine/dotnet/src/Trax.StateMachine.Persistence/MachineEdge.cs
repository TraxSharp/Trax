using System.Text.Json.Nodes;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// One edge of a <see cref="MachineGraph"/>: from a state, on a trigger, to a state, with the guard, reduction and
/// effect the IR names on it. A timer edge also carries how long its state is held.
/// </summary>
/// <param name="From">The state the edge leaves.</param>
/// <param name="Trigger">
/// The trigger that takes it: a command's name, the timer's trigger, or the outcome trigger (<c>Building.done</c>).
/// </param>
/// <param name="To">The state the edge enters.</param>
/// <param name="Kind">What takes the edge.</param>
internal sealed record MachineEdge(string From, string Trigger, string To, MachineEdgeKind Kind)
{
    /// <summary>The guard's message (<c>Because</c>), or the name of its rule; null when the edge is unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>The name of the edge's reduction, or null when it keeps the context.</summary>
    public string? Reduce { get; init; }

    /// <summary>The key prefix of the effect the edge runs exactly once, or null when it runs none.</summary>
    public string? Effect { get; init; }

    /// <summary>
    /// How long a timer edge's state is held: an ISO-8601 duration (<c>PT10M</c>), or a context field with its
    /// bounds. Null for any other edge.
    /// </summary>
    public string? Timer { get; init; }
}
