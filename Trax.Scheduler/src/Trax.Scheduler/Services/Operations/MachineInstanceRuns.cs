using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The train runs one state-machine instance invoked, newest first, as
/// <see cref="IOperationsService.GetMachineInstanceRunsAsync"/> returns them.
/// </summary>
/// <remarks>
/// A run is linked to the instance that queued it by its machine, its instance id and its owner
/// kind, which the run keeps after the instance has left the state. A system instance is unique
/// by machine and id, so every run so linked is its own. A user's draft is not: several users can
/// each hold a draft under one id, and a run does not record whose draft queued it. So a user's
/// draft lists only its live run, the one whose external id is the draft's own invoke token, and
/// never a run another user's draft under the same id may have queued.
/// </remarks>
/// <param name="Items">
/// The runs, newest first, at most <see cref="OperationsService.MachineInstanceRunCap"/>.
/// </param>
/// <param name="Capped">True when the instance invoked more runs than are listed.</param>
/// <param name="QueuedEntryId">
/// The work queue entry of the live run while it is still queued and so has no run row yet;
/// null otherwise.
/// </param>
public record MachineInstanceRuns(
    IReadOnlyList<MachineInstanceRun> Items,
    bool Capped,
    long? QueuedEntryId
);
