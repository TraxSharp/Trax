using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One state-machine instance read on its own, by <c>operations.machineInstance</c>. It has the
/// list item's fields and the runs the instance invoked, which only this lookup reads, so a page
/// of instances never pays for a lookup per row.
/// </summary>
/// <remarks>
/// Like the list item it carries no context and no owner key, and its runs carry no input or
/// output. A system instance lists every run it invoked; a user's draft lists only its live run,
/// because a run does not record which user's draft queued it and several users can each hold a
/// draft under one id.
/// </remarks>
/// <param name="RowId">The row's key.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <see langword="null"/> for one written before Trax recorded it.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
public record MachineInstanceDetail(
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
    /// <summary>
    /// The train runs the instance invoked, newest first, at most 50, with the one its state
    /// waits on marked <c>isLive</c>. A user's draft lists only its live run.
    /// </summary>
    public IReadOnlyList<MachineInstanceInvokedRun> InvokedRuns { get; init; } = [];

    /// <summary>True when the instance invoked more runs than <see cref="InvokedRuns"/> lists.</summary>
    public bool IsInvokedRunsCapped { get; init; }

    /// <summary>The instance's revision: 1 when it was created, one more for every write since.</summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The work queue entry of the run the state waits on while it is still queued, and so not
    /// yet in <see cref="InvokedRuns"/>; <see langword="null"/> otherwise.
    /// </summary>
    public long? QueuedInvokedRunEntryId { get; init; }

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

    /// <summary>The detail for <paramref name="record"/>, with the runs <paramref name="runs"/> lists.</summary>
    internal static MachineInstanceDetail From(
        MachineInstanceRecord record,
        MachineInstanceRuns? runs
    ) =>
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
            InvokedRuns = runs?.Items.Select(MachineInstanceInvokedRun.From).ToList() ?? [],
            IsInvokedRunsCapped = runs?.Capped ?? false,
            QueuedInvokedRunEntryId = runs?.QueuedEntryId,
            Revision = record.Revision,
            DueAt = record.DueAt,
            Overdue = record.Overdue,
            WaitingOn = MachineInstanceWaitingRun.From(record.WaitingOn),
            InStateSince = record.InStateSince,
            Stuck = record.Stuck,
        };
}
