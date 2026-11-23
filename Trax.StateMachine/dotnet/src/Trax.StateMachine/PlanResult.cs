using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The total result of a plan (<see cref="IrMachine.Plan"/>, <see cref="BuiltMachine{TState,TTrigger}.Plan"/>): the
/// events that reach the target, or why there are none. Never an exception.
/// </summary>
public abstract record PlanResult
{
    /// <summary>The events that take the snapshot to the target, in order; empty when it is there already.</summary>
    public sealed record Planned(IReadOnlyList<PlannedEvent> Events) : PlanResult;

    /// <summary>The search ran out of snapshots without reaching the target.</summary>
    public sealed record Unreachable : PlanResult;

    /// <summary>The search stopped at its depth or its size before it could tell.</summary>
    public sealed record Exceeded : PlanResult;

    /// <summary>
    /// No plan, but <see cref="Trigger"/> leaves from the snapshot <see cref="Events"/> reach on an edge that rests on a
    /// custom rule this engine holds no handler for. Send the events, then the trigger with no prediction, and plan
    /// again from the server's answer.
    /// </summary>
    public sealed record Undetermined(string Trigger, IReadOnlyList<PlannedEvent> Events)
        : PlanResult;

    /// <summary>
    /// The request cannot be planned: <see cref="Code"/> is <see cref="RefusalCode.InvalidInput"/> or
    /// <see cref="RefusalCode.InvalidContext"/> when the
    /// fields input or the snapshot's context nests more than 512 arrays and objects deep, <see cref="Target"/> is not
    /// a state, or <see cref="Trigger"/> is not one a client may send to move (one no transition fires on, one bound to
    /// the effect, an outcome, a timer, or a navigation trigger that is also the fields event). Exactly one of the three
    /// is set.
    /// </summary>
    public sealed record Invalid(string? Trigger = null, string? Target = null) : PlanResult
    {
        /// <summary>The refusal code of a fields input or a context nested too deep, or null.</summary>
        public RefusalCode? Code { get; init; }
    }

    private PlanResult() { }
}
