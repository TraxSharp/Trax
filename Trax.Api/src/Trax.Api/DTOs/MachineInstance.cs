using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One state-machine instance in the operator's list: which machine, who owns it, what state it
/// is in and when it got there.
/// </summary>
/// <remarks>
/// It carries no context and no owner key, on the list or on the detail
/// (<see cref="MachineInstanceDetail"/>). The context is an untyped JSON object, so nothing can
/// mask its sensitive parts, and no operator view names the user behind a row. Whether the
/// instance waits on a train run it invoked is a flag, not the run's token.
/// </remarks>
/// <param name="RowId">
/// The row's key. Pass it to <c>machineInstance</c> to read a user's draft, since several users
/// can each hold a draft under one id.
/// </param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <see langword="null"/> for one written before Trax recorded it.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
public record MachineInstance(
    long RowId,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    string State,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasLiveInvokedRun
)
{
    /// <summary>The instance's revision: 1 when it was created, one more for every write since.</summary>
    public long? Revision { get; init; }

    /// <summary>When the timer of its state fires, or <see langword="null"/> when that state has no timer.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>True when <see cref="DueAt"/> is past by more than the host's timer poll: the timer has not fired.</summary>
    public bool Overdue { get; init; }

    /// <summary>The run its state waits on and where that run is, or <see langword="null"/> when it waits on none.</summary>
    public MachineInstanceWaitingRun? WaitingOn { get; init; }

    /// <summary>
    /// When it entered its state, as far as Trax records it: until a transition log is kept, when it last
    /// changed (<c>updatedAt</c>). Show it as "last changed".
    /// </summary>
    public DateTimeOffset InStateSince { get; init; }

    /// <summary>True when it cannot move on its own: <see cref="Overdue"/>, or its run failed or was cancelled.</summary>
    public bool Stuck { get; init; }

    /// <summary>The list item for <paramref name="record"/>.</summary>
    internal static MachineInstance From(MachineInstanceRecord record) =>
        new(
            record.RowId,
            record.Machine,
            record.OwnerKind,
            record.Id,
            record.State,
            record.Version,
            record.CreatedAt,
            record.UpdatedAt,
            record.HasLiveInvokedRun
        )
        {
            Revision = record.Revision,
            DueAt = record.DueAt,
            Overdue = record.Overdue,
            WaitingOn = MachineInstanceWaitingRun.From(record.WaitingOn),
            InStateSince = record.InStateSince,
            Stuck = record.Stuck,
        };
}
