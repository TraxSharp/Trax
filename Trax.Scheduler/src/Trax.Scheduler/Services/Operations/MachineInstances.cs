using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Which state-machine instances to list, and which page of them. Used by
/// <see cref="IOperationsService.GetMachineInstancesAsync"/> and, for the filter alone,
/// <see cref="IOperationsService.CountMachineInstancesAsync"/>. Every filter is optional; the
/// list is newest first by when each instance was last written.
/// </summary>
/// <param name="Machine">Only instances of this machine (its id, matched exactly).</param>
/// <param name="State">Only instances in this state (the state's name, matched exactly).</param>
/// <param name="OwnerKind">Only instances a user owns, or only those the system owns.</param>
/// <param name="Skip">Offset into the ordered list.</param>
/// <param name="Take">
/// Page size, clamped to 1 through <see cref="OperationsService.MaxPageSize"/>.
/// </param>
public record MachineInstanceQuery(
    string? Machine = null,
    string? State = null,
    SnapshotOwnerKind? OwnerKind = null,
    int Skip = 0,
    int Take = 25
);

/// <summary>
/// Names one row of <c>trax.snapshot_draft</c> for an operator: the machine, the owner kind and
/// the instance id, and for a user's draft the row id as well.
/// </summary>
/// <remarks>
/// The owner kind is always part of the key, because a user can hold a draft under the same id
/// as a system instance. A system instance is unique by machine and id among system rows, so
/// <paramref name="RowId"/> is optional there. Several users can each hold a draft under one id,
/// and an operator is not shown whose a draft is, so a user's draft is named by its
/// <paramref name="RowId"/> too, which every listed instance carries.
/// </remarks>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Who owns the row.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="RowId">
/// The row's surrogate key, as <see cref="MachineInstanceRecord.RowId"/> gives it. Required for a
/// user's draft; for a system instance, when given, it must match too.
/// </param>
public record MachineInstanceKey(
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    long? RowId = null
);

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
);

/// <summary>A page of state-machine instances, newest first by when each was last written.</summary>
/// <param name="Items">The instances on this page.</param>
/// <param name="Skip">The offset used.</param>
/// <param name="Take">The page size used, after clamping.</param>
public record MachineInstancePage(IReadOnlyList<MachineInstanceRecord> Items, int Skip, int Take);

/// <summary>
/// How many instances match a <see cref="MachineInstanceQuery"/>, counted up to
/// <see cref="OperationsService.MachineInstanceCountCap"/>.
/// </summary>
/// <param name="Count">
/// The number of matching instances; when <paramref name="Capped"/> is set, the cap rather than
/// the number.
/// </param>
/// <param name="Capped">
/// True when more instances match than the cap, so the count is a lower bound. The counts by
/// state (<see cref="IOperationsService.GetMachineInstanceStateCountsAsync"/>) are never capped.
/// </param>
public record MachineInstanceTotal(int Count, bool Capped);

/// <summary>How many instances of one machine are in one state, for one owner kind.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The state.</param>
/// <param name="OwnerKind">Who owns the instances counted.</param>
/// <param name="Count">How many there are.</param>
public record MachineInstanceStateCount(
    string Machine,
    string State,
    SnapshotOwnerKind OwnerKind,
    long Count
);
