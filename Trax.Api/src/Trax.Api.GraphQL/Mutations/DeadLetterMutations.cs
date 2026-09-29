using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Dead letter management mutations: requeue, acknowledge, and batch operations.
/// </summary>
public class DeadLetterMutations
{
    /// <summary>
    /// Queues a new run for one dead letter's manifest and marks the dead letter retried. Only a dead
    /// letter awaiting intervention can be requeued, and a manifest holds one queued entry at a time:
    /// if it already has one, the result reports failure and the dead letter stays awaiting intervention.
    /// </summary>
    public async Task<DeadLetterOperationResult> RequeueDeadLetter(
        long id,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueDeadLetterAsync(id, ct);

    /// <summary>
    /// Marks one dead letter acknowledged without running it again, recording <c>note</c> as the
    /// reason.
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
    /// </summary>
    public async Task<BatchDeadLetterResult> RequeueDeadLetters(
        long[] ids,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueDeadLettersAsync(ids, ct);

    /// <summary>
    /// Marks the listed dead letters acknowledged without running them again, recording <c>note</c>
    /// on each.
    /// </summary>
    public async Task<BatchDeadLetterResult> AcknowledgeDeadLetters(
        long[] ids,
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeDeadLettersAsync(ids, note, ct);

    /// <summary>
    /// Requeues every dead letter awaiting intervention, creating at most one work queue entry per
    /// manifest as <c>requeueDeadLetters</c> does.
    /// </summary>
    public async Task<BatchDeadLetterResult> RequeueAllDeadLetters(
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueAllDeadLettersAsync(ct);

    /// <summary>
    /// Marks every dead letter awaiting intervention acknowledged, recording <c>note</c> on each.
    /// </summary>
    public async Task<BatchDeadLetterResult> AcknowledgeAllDeadLetters(
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeAllDeadLettersAsync(note, ct);
}
