using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// What <see cref="IOperationsService.CancelMachineInstanceAsync"/> did, or why it did nothing.
/// The first three are successes; the rest are refusals and change nothing.
/// </summary>
public enum MachineInstanceCancelOutcome
{
    /// <summary>
    /// The live run was still queued and is cancelled before it started, and this call moved the
    /// instance through its <c>OnCancelled</c> edge.
    /// </summary>
    Moved,

    /// <summary>
    /// The live run was still queued and is cancelled before it started. The instance moves
    /// through its <c>OnCancelled</c> edge when a host that registers the machine applies the
    /// outcome (this host does not, or another delivery got there first).
    /// </summary>
    RunCancelled,

    /// <summary>
    /// The live run was already dispatched, and its cancel is requested: it stops at its next
    /// junction, ends Cancelled, and the instance then moves through its <c>OnCancelled</c> edge.
    /// </summary>
    CancelRequested,

    /// <summary>The instance is owned by a user; operators may cancel only system-owned instances.</summary>
    UserOwned,

    /// <summary>No system-owned instance has this machine and id.</summary>
    NotFound,

    /// <summary>The instance's state waits on no train run, so there is nothing to cancel.</summary>
    NoLiveRun,

    /// <summary>
    /// The live run has already ended; its outcome is being applied, and a cancel cannot change it.
    /// </summary>
    RunEnded,
}
