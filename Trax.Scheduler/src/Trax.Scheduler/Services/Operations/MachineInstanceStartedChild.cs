using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>The child machine instance one transition spawned, named as a lookup of it names it. Never its owner.</summary>
/// <param name="Machine">The child's machine.</param>
/// <param name="OwnerKind">Who owns the child: the parent's owner.</param>
/// <param name="Id">The child's id.</param>
/// <param name="RowId">The child's row.</param>
public record MachineInstanceStartedChild(
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    long RowId
);
