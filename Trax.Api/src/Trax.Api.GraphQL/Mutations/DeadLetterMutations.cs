using Trax.Api.DTOs;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.TraxScheduler;
using DeadLetterRequeueJob = Trax.Api.DTOs.DeadLetterRequeueJob;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Dead letter management mutations: requeue, acknowledge, and batch operations.
/// </summary>
public class DeadLetterMutations
{
    /// <summary>
    /// The longest acknowledgement note accepted, in characters: the scheduler's
    /// <see cref="TraxScheduler.MaxAcknowledgeNoteLength"/>, which refuses a longer note for the
    /// dashboard and this surface alike. A note is an operator's reason, a sentence or a
    /// paragraph; <c>acknowledgeAllDeadLetters</c> writes it onto every awaiting row, so an
    /// unbounded one multiplies into the table by the size of the backlog. A longer note is
    /// refused, not cut, so what is stored is what the operator wrote.
    /// </summary>
    public const int MaxNoteLength = TraxScheduler.MaxAcknowledgeNoteLength;

    /// <summary>
    /// Queues a new run for one dead letter's manifest and marks the dead letter retried. Only a dead
    /// letter awaiting intervention can be requeued, and a manifest holds one queued entry at a time:
    /// if it already has one, the result reports failure and the dead letter stays awaiting intervention.
    /// The new run replays the decisions of the manifest's failed run when that is sound, as a retry
    /// does; <c>askAfresh: true</c> makes it ask its deciders again.
    /// </summary>
    public async Task<DeadLetterOperationResult> RequeueDeadLetter(
        long id,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct,
        bool askAfresh = false
    ) =>
        // The overload is called only when asked, so a host whose scheduler predates it keeps the
        // default path.
        askAfresh
            ? await scheduler.RequeueDeadLetterAsync(id, askAfresh: true, ct)
            : await scheduler.RequeueDeadLetterAsync(id, ct);

    /// <summary>
    /// Marks one dead letter acknowledged without running it again, recording <c>note</c> as the
    /// reason. A note longer than 1,000 characters is refused by the scheduler and nothing changes.
    /// </summary>
    public async Task<DeadLetterOperationResult> AcknowledgeDeadLetter(
        long id,
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeDeadLetterAsync(id, note, ct);

    /// <summary>
    /// Requeues the listed dead letters. At most one work queue entry is created per manifest: dead
    /// letters that share a manifest are folded into one entry and all resolved, and a dead letter
    /// whose manifest already has a queued entry is skipped and left awaiting intervention.
    /// <c>askAfresh: true</c> makes every new run ask its deciders again rather than replay.
    /// </summary>
    public async Task<BatchDeadLetterResult> RequeueDeadLetters(
        long[] ids,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct,
        bool askAfresh = false
    ) =>
        askAfresh
            ? await scheduler.RequeueDeadLettersAsync(ids, askAfresh: true, ct)
            : await scheduler.RequeueDeadLettersAsync(ids, ct);

    /// <summary>
    /// Marks the listed dead letters acknowledged without running them again, recording <c>note</c>
    /// on each. A note longer than 1,000 characters is refused: <c>count</c> is 0 and nothing changes.
    /// </summary>
    public async Task<BatchDeadLetterResult> AcknowledgeDeadLetters(
        long[] ids,
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeDeadLettersAsync(ids, note, ct);

    /// <summary>
    /// Starts requeueing every dead letter awaiting intervention, creating at most one work queue
    /// entry per manifest as <c>requeueDeadLetters</c> does, and returns at once with the job's
    /// handle rather than holding the request for the whole backlog. Read
    /// <c>operations.deadLetters.requeueAllJob(id)</c> on the same node until its status is no
    /// longer <c>RUNNING</c>. The fold is not tied to this request: it finishes after the client
    /// goes away, and only the node shutting down stops it. While one is running on this node,
    /// this returns that one with <c>started: false</c>. <c>askAfresh: true</c> makes every new run
    /// ask its deciders again rather than replay.
    /// </summary>
    /// <remarks>
    /// The job is the scheduler's <see cref="IDeadLetterRequeueJobs"/>, the one the dashboard's
    /// Requeue All starts.
    /// </remarks>
    public async Task<DeadLetterRequeueJob> RequeueAllDeadLetters(
        [Service] IDeadLetterRequeueJobs jobs,
        CancellationToken ct,
        bool askAfresh = false
    ) => DeadLetterRequeueJob.From(await jobs.StartAsync(askAfresh, ct));

    /// <summary>
    /// Marks every dead letter awaiting intervention acknowledged, recording <c>note</c> on each. A
    /// note longer than 1,000 characters is refused: <c>count</c> is 0 and nothing changes.
    /// </summary>
    public async Task<BatchDeadLetterResult> AcknowledgeAllDeadLetters(
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeAllDeadLettersAsync(note, ct);
}
