namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// What needs an operator's attention right now, read in one call by
/// <see cref="IOperationsService.GetAttentionAsync"/>: whether the scheduler is paused, the dead
/// letters awaiting intervention, the trains failing more than usual, the stuck state-machine
/// instances, the queue backlog and the hosts that stopped beating. Each entry carries
/// <c>Link</c>, the operations console's path and query that lists what it counts.
/// </summary>
/// <param name="Scheduler">Whether the manifest manager and the job dispatcher are switched on.</param>
/// <param name="DeadLetters">
/// The dead letters awaiting intervention, one group per train and reason, largest first, at most
/// <see cref="OperationsService.AttentionListCap"/> groups.
/// </param>
/// <param name="FailingTrains">
/// The trains whose failure rate in the last hour is above their rate over the six days and 23
/// hours before it, highest rate first, at most <see cref="OperationsService.AttentionListCap"/>.
/// </param>
/// <param name="StuckInstances">
/// The stuck state-machine instances, one group per machine and state, largest first, at most
/// <see cref="OperationsService.AttentionListCap"/> groups.
/// </param>
/// <param name="Queue">How much queued work is waiting and how long the oldest has waited.</param>
/// <param name="OfflineHosts">
/// The hosts that beat in the last day and have since missed three beats, most recently seen
/// first, at most <see cref="OperationsService.AttentionListCap"/>.
/// </param>
/// <param name="AsOf">When the read was made (UTC); ages and rates are measured from it.</param>
public sealed record Attention(
    AttentionScheduler Scheduler,
    IReadOnlyList<AttentionDeadLetterGroup> DeadLetters,
    IReadOnlyList<AttentionFailingTrain> FailingTrains,
    IReadOnlyList<AttentionStuckInstances> StuckInstances,
    AttentionQueue Queue,
    IReadOnlyList<AttentionHost> OfflineHosts,
    DateTime AsOf
);
