using System.Text.Json.Nodes;

namespace Trax.StateMachine.Persistence;

/// <summary>What takes a <see cref="MachineEdge"/>: a command someone sends, the state's timer, or an invoked train's outcome.</summary>
internal enum MachineEdgeKind
{
    Command,
    Timer,
    Outcome,
}
