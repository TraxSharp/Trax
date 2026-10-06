namespace Trax.Scheduler.Services.DeadLetterRequeue;

/// <summary>
/// Requeues every dead letter awaiting intervention as a background job on this node, so the
/// fold over a large backlog is bound by neither a request nor an operator's dashboard circuit.
/// The dashboard's Requeue All and the GraphQL API's <c>requeueAllDeadLetters</c> both start the
/// job here and read it back by id (central <c>docs/0022</c>; Trax.Api
/// <c>docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md</c>).
/// </summary>
/// <remarks>
/// The fold runs <c>ITraxScheduler.RequeueAllDeadLettersAsync</c> under the host's shutdown token,
/// never the caller's, so only the node shutting down stops it, between pages. Job state is in this
/// node's memory: another node does not know the job, and a restart forgets it. Nothing is lost by
/// that, because the fold is resumable (see <see cref="DeadLetterRequeueJob"/>). One fold runs per
/// node at a time, a finished job is kept for 24 hours, and at most 100 finished jobs are kept.
/// Registered as a singleton by <c>AddScheduler</c>.
/// </remarks>
public interface IDeadLetterRequeueJobs
{
    /// <summary>
    /// Counts the dead letters awaiting intervention, starts a requeue-all fold over them in the
    /// background and returns its handle at once. While one is already running on this node, this
    /// starts nothing and returns the running one with <see cref="DeadLetterRequeueJob.Started"/>
    /// <c>false</c>; when the running one is in the other mode than <paramref name="askAfresh"/>,
    /// its message also says this request was not folded into it.
    /// </summary>
    /// <param name="askAfresh">True makes every new run ask its deciders afresh rather than replay.</param>
    /// <param name="ct">Cancels the count only; the fold, once started, is not tied to it.</param>
    Task<DeadLetterRequeueJob> StartAsync(bool askAfresh = false, CancellationToken ct = default);

    /// <summary>
    /// The job with this id, or <c>null</c> when this node does not know it: it was started on
    /// another node, this node has restarted since, or it finished more than 24 hours ago.
    /// </summary>
    DeadLetterRequeueJob? Get(Guid id);
}
