using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One event of the operator's live feed, <c>onMachineInstanceChanged</c>: a state-machine
/// instance as it was just written, or, with <see cref="Deleted"/> set, as it was last seen
/// before it was deleted or expired.
/// </summary>
/// <remarks>
/// Like the list item it carries no context and no owner key: an operator sees that an instance
/// moved and where to, never what it holds or whose it is.
/// </remarks>
/// <param name="RowId">The row's key, which names one incarnation of the instance.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <see langword="null"/> where that is not known.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
/// <param name="Revision">Its revision: 1 when it was created, one more for every write since.</param>
/// <param name="Deleted">True when the instance was deleted or expired; nothing follows for this row.</param>
public record MachineInstanceChange(
    long RowId,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    string State,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasLiveInvokedRun,
    long? Revision,
    bool Deleted
)
{
    /// <summary>
    /// True when writes may have been missed (the host's change listener started or reconnected, or this feed fell
    /// behind): the event names no instance, and the subscriber reads the instances it shows again.
    /// </summary>
    public bool Reload { get; init; }

    /// <summary>The feed event for <paramref name="record"/>.</summary>
    internal static MachineInstanceChange From(MachineInstanceRecord record) =>
        new(
            record.RowId,
            record.Machine,
            record.OwnerKind,
            record.Id,
            record.State,
            record.Version,
            record.CreatedAt,
            record.UpdatedAt,
            record.HasLiveInvokedRun,
            record.Revision,
            record.Deleted
        )
        {
            Reload = record.Reload,
        };
}
