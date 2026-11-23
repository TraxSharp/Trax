using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// The state-machine instance whose invoking state queued a run, as
/// <see cref="ExecutionDetail.InvokedBy"/> names it. Never the context of the instance, and never
/// whose a user's draft is.
/// </summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="InstanceId">The instance's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="RowId">
/// The instance's row while it still waits on this run, the key <c>machineInstance</c> takes
/// with a user's draft; <see langword="null"/> once it has moved on, or when the row is gone.
/// </param>
public sealed record RunInvoker(
    string Machine,
    Guid InstanceId,
    SnapshotOwnerKind OwnerKind,
    long? RowId
);
