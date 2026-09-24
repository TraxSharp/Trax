namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService.QueueAsync"/> when a train
/// that defers promotion had its staged entry cancelled while its <c>OnQueue</c> hook ran.
/// </summary>
/// <remarks>
/// The work will not run, but the hook returned, so its side-effect may already have landed. That
/// is the one enqueue failure a caller may need to compensate for, which is why it has its own
/// type rather than the <see cref="InvalidOperationException"/> the other refusals share.
///
/// The entry is cancelled by an operator, or by the stale-staged sweep when the hook outlives
/// <c>StaleStagedEntryTimeout</c>. If the sweep promoted it instead (a host that opted in), the
/// entry runs and the enqueue succeeds.
///
/// Derives from <see cref="InvalidOperationException"/>, so code that caught that type for this
/// case still catches it.
/// </remarks>
public class QueuedWorkCancelledException : InvalidOperationException
{
    /// <summary>The cancelled work queue entry.</summary>
    public long WorkQueueId { get; }

    /// <summary>The fully qualified service type name of the train that was being queued.</summary>
    public string TrainName { get; }

    public QueuedWorkCancelledException(long workQueueId, string trainName)
        : base(
            $"Work queue entry {workQueueId} for {trainName} was cancelled before its OnQueue "
                + "hook returned, so it will not run. The hook's side-effect may already have "
                + "been applied."
        )
    {
        WorkQueueId = workQueueId;
        TrainName = trainName;
    }
}
