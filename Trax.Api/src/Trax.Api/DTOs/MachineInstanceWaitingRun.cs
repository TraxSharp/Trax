using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// The train run a state-machine instance's state waits on: which run, and where it is. Never the
/// instance's invoke token.
/// </summary>
/// <param name="ExecutionId">The run's id, or <see langword="null"/> while it is queued and has no run yet.</param>
/// <param name="TrainState">
/// Where the run is: its state once dispatched, <c>PENDING</c> while its entry is queued,
/// <c>CANCELLED</c> when the entry was cancelled before it started; <see langword="null"/> when neither is found.
/// </param>
/// <param name="WorkQueueEntryId">The run's work queue entry, or <see langword="null"/> when none is found.</param>
public record MachineInstanceWaitingRun(
    long? ExecutionId,
    TrainState? TrainState,
    long? WorkQueueEntryId
)
{
    internal static MachineInstanceWaitingRun? From(MachineInstanceWaitingOn? waiting) =>
        waiting is null
            ? null
            : new(waiting.ExecutionId, waiting.TrainState, waiting.WorkQueueEntryId);
}
