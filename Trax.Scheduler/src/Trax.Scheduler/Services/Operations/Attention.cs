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

/// <summary>Whether the scheduler's two loops are switched on, as the live settings hold them.</summary>
/// <param name="ManifestManagerEnabled">Whether the manifest manager queues scheduled work.</param>
/// <param name="JobDispatcherEnabled">Whether the job dispatcher moves queued work to workers.</param>
/// <param name="Link">The console's scheduler settings, where both are switched.</param>
public sealed record AttentionScheduler(
    bool ManifestManagerEnabled,
    bool JobDispatcherEnabled,
    string Link
)
{
    /// <summary>True when either loop is switched off.</summary>
    public bool Paused => !ManifestManagerEnabled || !JobDispatcherEnabled;
}

/// <summary>The dead letters awaiting intervention that share a train and a reason.</summary>
/// <param name="TrainName">The train their manifests run (the train interface's full name).</param>
/// <param name="Reason">Why each was dead-lettered.</param>
/// <param name="Count">How many there are.</param>
/// <param name="OldestDeadLetteredAt">When the oldest of them was dead-lettered (UTC).</param>
/// <param name="Link">The console's dead letters, filtered to this train, reason and awaiting intervention.</param>
public sealed record AttentionDeadLetterGroup(
    string TrainName,
    string Reason,
    long Count,
    DateTime OldestDeadLetteredAt,
    string Link
);

/// <summary>
/// A train failing more often in the last hour than over the week before it. A rate is failed runs
/// over finished runs (completed or failed); cancelled runs count in neither.
/// </summary>
/// <param name="TrainName">The train (the train interface's full name).</param>
/// <param name="FailedLastHour">Runs that started in the last hour and failed.</param>
/// <param name="FinishedLastHour">Runs that started in the last hour and completed or failed.</param>
/// <param name="LastHourFailureRate">The last hour's failure rate, 0 to 1.</param>
/// <param name="BaselineFailureRate">
/// The failure rate of runs that started in the seven days before the last hour, 0 to 1; 0 when
/// none finished.
/// </param>
/// <param name="Link">The console's runs, filtered to this train's failed runs.</param>
public sealed record AttentionFailingTrain(
    string TrainName,
    long FailedLastHour,
    long FinishedLastHour,
    double LastHourFailureRate,
    double BaselineFailureRate,
    string Link
);

/// <summary>
/// The stuck instances of one machine in one state, system instances and users' drafts together.
/// Stuck is the rule <see cref="MachineInstanceRecord.Stuck"/> states.
/// </summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The state they are stuck in.</param>
/// <param name="Count">How many.</param>
/// <param name="Link">The console's machine page, filtered to this state's stuck instances.</param>
public sealed record AttentionStuckInstances(string Machine, string State, long Count, string Link);

/// <summary>The queued work that is due and has not been dispatched.</summary>
/// <param name="Queued">
/// How many entries are queued, confirmed and due (no scheduled time, or one that has passed): what
/// the dispatcher would offer to a worker now.
/// </param>
/// <param name="OldestQueuedAt">When the oldest of them was queued (UTC); null when none is.</param>
/// <param name="OldestAgeSeconds">How long the oldest has waited, in seconds; null when none is queued.</param>
/// <param name="Link">The console's queue, filtered to queued entries.</param>
public sealed record AttentionQueue(
    long Queued,
    DateTime? OldestQueuedAt,
    double? OldestAgeSeconds,
    string Link
);

/// <summary>A host that has missed three heartbeats in a row.</summary>
/// <param name="InstanceId">The process's identity, as stamped on its runs.</param>
/// <param name="HostName">The machine it ran on.</param>
/// <param name="LastHeartbeatAt">When it last beat (UTC).</param>
/// <param name="RunsDispatcher">Whether it ran the job dispatcher at its last beat.</param>
/// <param name="Link">The console's hosts.</param>
public sealed record AttentionHost(
    string InstanceId,
    string HostName,
    DateTime LastHeartbeatAt,
    bool RunsDispatcher,
    string Link
);
