using Trax.Core.Exceptions;
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
)
{
    /// <summary>
    /// True for only the instances that are stuck (see <see cref="MachineInstanceRecord.Stuck"/>), false for only
    /// those that are not; null for both.
    /// </summary>
    public bool? Stuck { get; init; }
}
