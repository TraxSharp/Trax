namespace Trax.Scheduler.Services.Operations;

/// <summary>One piece of work a drain waits on, in <see cref="DrainStatus.Busy"/>.</summary>
/// <param name="Kind">
/// <c>run</c>: a run that is Pending or InProgress, other than one whose job waits in the queue
/// unclaimed. <c>instance</c>: a state-machine instance that is not at rest, because an effect is
/// in flight on it or the run it invoked is busy.
/// </param>
/// <param name="Id">The run's database id, or the instance's machine and id joined by <c>/</c>.</param>
/// <param name="Name">The run's train, or the instance's machine.</param>
/// <param name="State">The run's state, or the instance's state and what it waits on.</param>
/// <param name="Since">When the run started, or when the instance was last written, in UTC.</param>
/// <param name="Host">The host the run is on, when it records one; null for an instance.</param>
public record DrainBusyWork(
    string Kind,
    string Id,
    string Name,
    string State,
    DateTime Since,
    string? Host
);
