using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>The child machine instance a transition spawned, named as <c>machineInstance</c> takes it. Never its owner.</summary>
/// <param name="Machine">The child's machine.</param>
/// <param name="OwnerKind">Who owns the child: the parent's owner.</param>
/// <param name="Id">The child's id.</param>
/// <param name="RowId">The child's row, which a lookup of a user's draft needs.</param>
public record MachineInstanceTransitionChild(
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    long RowId
);
