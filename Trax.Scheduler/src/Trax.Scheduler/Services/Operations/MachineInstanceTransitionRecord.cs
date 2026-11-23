using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One transition in a state-machine instance's history, as an operator reads it: what moved it, from where to where,
/// who and when, and what it started. Never the instance's context, its owner's key or a run's token. See
/// <see cref="IOperationsService.GetMachineInstanceTransitionsAsync"/>.
/// </summary>
/// <param name="Revision">The revision the write gave the instance.</param>
/// <param name="Kind">What wrote it: a start, a command, a save, a timer or a run's outcome.</param>
/// <param name="Trigger">
/// The command that moved it (an event, a timer's event, or <c>State.done</c>, <c>State.failed</c> or
/// <c>State.cancelled</c> for a run's outcome), or null for a creation, a save, or an outcome that could not be applied.
/// </param>
/// <param name="Actor">Who made it: the owner, host code, or the machine itself. Never which user.</param>
/// <param name="FromState">The state it left, or null for its creation.</param>
/// <param name="ToState">The state it entered.</param>
/// <param name="At">When it was written.</param>
public record MachineInstanceTransitionRecord(
    long Revision,
    SnapshotTransitionKind Kind,
    string? Trigger,
    SnapshotTransitionActor Actor,
    string? FromState,
    string ToState,
    DateTimeOffset At
)
{
    /// <summary>
    /// When the transition before it was written, so <see cref="At"/> minus this is how long the instance spent in
    /// <see cref="FromState"/> since it was last written; null for the oldest recorded transition.
    /// </summary>
    public DateTimeOffset? PreviousAt { get; init; }

    /// <summary>The train run entering <see cref="ToState"/> queued, or null when it queued none.</summary>
    public MachineInstanceStartedRun? StartedRun { get; init; }

    /// <summary>The child machine instance entering <see cref="ToState"/> spawned, or null when it spawned none.</summary>
    public MachineInstanceStartedChild? StartedChild { get; init; }
}
