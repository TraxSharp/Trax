using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

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
)
{
    /// <summary>How many of them are stuck: a timer overdue, or waiting on a run that failed or was cancelled.</summary>
    public long Stuck { get; init; }
}
