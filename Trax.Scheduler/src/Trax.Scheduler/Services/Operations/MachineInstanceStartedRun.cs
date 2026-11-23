using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The train run one transition queued, as an operator reads it: which run, and where it is now. Never its token.
/// </summary>
/// <param name="ExecutionId">The run's id, or null while it is still queued and has no run yet.</param>
/// <param name="TrainState">
/// Where the run is: its state once dispatched, <c>Pending</c> while its entry is queued, <c>Cancelled</c> when its
/// entry was cancelled before it started; null when neither the run nor its entry is found any more.
/// </param>
/// <param name="WorkQueueEntryId">The run's work queue entry, or null when none is found.</param>
public record MachineInstanceStartedRun(
    long? ExecutionId,
    TrainState? TrainState,
    long? WorkQueueEntryId
);
