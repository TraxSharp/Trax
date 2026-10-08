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
/// <param name="CreatedAt">When it was created, or <c>null</c> for one written before Trax recorded it.</param>
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
        );
}

/// <summary>
/// One state-machine instance read on its own, by <c>operations.machineInstance</c>. It has the
/// list item's fields, and is the type the runs an instance invoked are read from, so a page of
/// instances never pays for a lookup per row.
/// </summary>
/// <remarks>Like the list item it carries no context and no owner key.</remarks>
/// <param name="RowId">The row's key.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <c>null</c> for one written before Trax recorded it.</param>
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
    /// <summary>The detail for <paramref name="record"/>.</summary>
    internal static MachineInstanceDetail From(MachineInstanceRecord record) =>
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
        );
}

/// <summary>How many instances of one machine are in one state, for one owner kind.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The state.</param>
/// <param name="OwnerKind">Who owns the instances counted.</param>
/// <param name="Count">How many there are.</param>
public record MachineInstanceCount(
    string Machine,
    string State,
    SnapshotOwnerKind OwnerKind,
    long Count
);
