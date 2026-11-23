using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One transition in a state-machine instance's history: the command that moved it, from where to where, who made
/// it, when, and the run or child machine it started.
/// </summary>
/// <remarks>
/// Like the instance it carries no context and no owner key: <see cref="Actor"/> says whether the owner, host code
/// or the machine itself made it, never which user. A run it started is named by its ids, never by its token.
/// </remarks>
/// <param name="Revision">The revision the write gave the instance.</param>
/// <param name="Kind">What wrote it: a start, a command, a save, a timer or a run's outcome.</param>
/// <param name="Trigger">
/// The command that moved it (an event, a timer's event, or <c>State.done</c>, <c>State.failed</c> or
/// <c>State.cancelled</c> for a run's outcome), or <see langword="null"/> for a creation, a save, or an outcome that could not
/// be applied.
/// </param>
/// <param name="Actor">Who made it: the owner, host code, or the machine itself.</param>
/// <param name="FromState">The state it left, or <see langword="null"/> for the instance's creation.</param>
/// <param name="ToState">The state it entered.</param>
/// <param name="At">When it was written.</param>
public record MachineInstanceTransition(
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
    /// When the transition before it was written: <see cref="At"/> minus this is how long the instance stayed in
    /// <see cref="FromState"/> since its previous write. <see langword="null"/> for the oldest recorded one.
    /// </summary>
    public DateTimeOffset? PreviousAt { get; init; }

    /// <summary>The train run entering <see cref="ToState"/> queued, or <see langword="null"/>.</summary>
    public MachineInstanceTransitionRun? StartedRun { get; init; }

    /// <summary>The child machine instance entering <see cref="ToState"/> spawned, or <see langword="null"/>.</summary>
    public MachineInstanceTransitionChild? StartedChild { get; init; }

    internal static MachineInstanceTransition From(MachineInstanceTransitionRecord record) =>
        new(
            record.Revision,
            record.Kind,
            record.Trigger,
            record.Actor,
            record.FromState,
            record.ToState,
            record.At
        )
        {
            PreviousAt = record.PreviousAt,
            StartedRun = record.StartedRun is { } run
                ? new MachineInstanceTransitionRun(
                    run.ExecutionId,
                    run.TrainState,
                    run.WorkQueueEntryId
                )
                : null,
            StartedChild = record.StartedChild is { } child
                ? new MachineInstanceTransitionChild(
                    child.Machine,
                    child.OwnerKind,
                    child.Id,
                    child.RowId
                )
                : null,
        };
}
