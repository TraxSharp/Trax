using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One state-machine instance as an operator reads it: where it is and when it got there, never
/// what it holds.
/// </summary>
/// <remarks>
/// The snapshot's context is left out on purpose: it is an untyped JSON object, so nothing can
/// mask the sensitive parts of it, and an operator sees an instance's state, timestamps, owner
/// kind and runs, not its data (central <c>docs/0046</c>). The owning user's key is left out too:
/// no other operator view names the user behind a row, and a host's key can be an email address
/// or another identifier the operator has no need for. The invoke token is reduced to whether
/// one is held.
/// </remarks>
/// <param name="RowId">
/// The row's surrogate key. It names one row whoever owns it, so it is how an operator's lookup
/// tells apart users' drafts that share an id (<see cref="MachineInstanceKey.RowId"/>).
/// </param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">
/// When the row was created; null for a row written before Trax recorded it.
/// </param>
/// <param name="UpdatedAt">When the row was last written.</param>
/// <param name="HasLiveInvokedRun">
/// True while the state it is in has invoked a train run whose outcome it still waits for.
/// </param>
public record MachineInstanceRecord(
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
    /// <summary>The instance's revision: 1 when the row was created, one more for every write since.</summary>
    public long? Revision { get; init; }

    /// <summary>
    /// True on the live feed (<see cref="IOperationsService.WatchMachineInstancesAsync"/>) when the instance was
    /// deleted or expired: the record is the instance as it was last seen, and nothing follows it for this row. Always
    /// false on a read.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or null when that state has no timer. Read by the list
    /// and the lookup; null on the live feed.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// True when <see cref="DueAt"/> is past by more than the host's timer poll
    /// (<c>StateMachineOptions.InvokeOutcomeSweepInterval</c>): the timer should have fired and has not. Read by the
    /// list and the lookup; false on the live feed.
    /// </summary>
    public bool Overdue { get; init; }

    /// <summary>
    /// The run the instance's state waits on, or null when it waits on none. Read by the list and the lookup; null on
    /// the live feed.
    /// </summary>
    public MachineInstanceWaitingOn? WaitingOn { get; init; }

    /// <summary>
    /// True when the instance cannot move on its own: it is <see cref="Overdue"/>, or the run it waits on failed or
    /// was cancelled and its outcome was never applied. How long an instance has sat in its state is not counted.
    /// Read by the list and the lookup; false on the live feed.
    /// </summary>
    public bool Stuck { get; init; }

    /// <summary>
    /// When the instance entered its state, as far as Trax records it. No transition log is kept yet, so this is
    /// <see cref="UpdatedAt"/>, when the instance last changed: a write that kept the state moves it too. Show it as
    /// "last changed".
    /// </summary>
    public DateTimeOffset InStateSince => UpdatedAt;

    /// <summary>
    /// True on the live feed (<see cref="IOperationsService.WatchMachineInstancesAsync"/>) when writes may have been
    /// missed (the host's change listener started, reconnected, or the reader fell behind): the record names no
    /// instance, and the reader reads what it shows again. Always false on a read.
    /// </summary>
    public bool Reload { get; init; }
}
