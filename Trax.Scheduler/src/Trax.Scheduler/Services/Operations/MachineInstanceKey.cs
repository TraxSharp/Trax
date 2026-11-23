using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

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
