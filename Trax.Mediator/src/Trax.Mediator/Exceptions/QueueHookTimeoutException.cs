namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService.QueueAsync"/> when a train's
/// <c>OnQueue</c> hook runs longer than <c>MaxQueueHookDuration</c>. The enqueue is rolled back:
/// no entry is written, and nothing the hook wrote on the enqueue's context is kept.
/// </summary>
/// <remarks>
/// The hook's token is cancelled at the limit, but the hook may still be running when this is
/// thrown, and a side-effect it writes elsewhere may land after it. Derives from
/// <see cref="InvalidOperationException"/>, the type the other enqueue refusals share.
/// See mediator/0004.
/// </remarks>
public class QueueHookTimeoutException : InvalidOperationException
{
    /// <summary>The fully qualified service type name of the train that was being queued.</summary>
    public string TrainName { get; }

    /// <summary>The limit the hook ran past.</summary>
    public TimeSpan Limit { get; }

    public QueueHookTimeoutException(string trainName, TimeSpan limit)
        : base(
            $"{trainName}.OnQueue ran longer than {limit.TotalSeconds:0.###}s, the limit set by "
                + "MaxQueueHookDuration, so the enqueue was rolled back. A hook that has to wait on "
                + "something slow can defer promotion instead (DeferQueuePromotion)."
        )
    {
        TrainName = trainName;
        Limit = limit;
    }
}
