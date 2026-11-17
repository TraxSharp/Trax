using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

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
)
{
    /// <summary>How many of them are stuck (see <see cref="MachineInstanceRecord.Stuck"/>).</summary>
    public long Stuck { get; init; }
}
