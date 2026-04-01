using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Dead letter management mutations: requeue, acknowledge, and batch operations.
/// </summary>
public class DeadLetterMutations
{
    public async Task<DeadLetterOperationResult> RequeueDeadLetter(
        long id,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueDeadLetterAsync(id, ct);

    public async Task<DeadLetterOperationResult> AcknowledgeDeadLetter(
        long id,
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeDeadLetterAsync(id, note, ct);

    public async Task<BatchDeadLetterResult> RequeueDeadLetters(
        long[] ids,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueDeadLettersAsync(ids, ct);

    public async Task<BatchDeadLetterResult> AcknowledgeDeadLetters(
        long[] ids,
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeDeadLettersAsync(ids, note, ct);

    public async Task<BatchDeadLetterResult> RequeueAllDeadLetters(
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.RequeueAllDeadLettersAsync(ct);

    public async Task<BatchDeadLetterResult> AcknowledgeAllDeadLetters(
        string note,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    ) => await scheduler.AcknowledgeAllDeadLettersAsync(note, ct);
}
