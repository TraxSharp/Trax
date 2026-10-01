using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations for the work queue: queue a train for execution, run one now, and cancel queued entries.
/// Thin wrappers around <see cref="IOperationsService"/>, which the dashboard also calls. Both
/// share validation and the enqueue path; the train's own authorization applies here, while the
/// dashboard enqueues as the admin surface its host gates (see docs/0017).
/// </summary>
public class WorkQueueMutations
{
    /// <summary>
    /// Creates a new work queue entry that the dispatcher will pick up.
    /// </summary>
    public async Task<OperationResponse> QueueTrain(
        QueueTrainInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.QueueTrainAsync(input, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Runs a train now, through the same <see cref="IOperationsService.RunTrainAsync"/> call as
    /// the dashboard's Run dialog: its Pending execution row is written and handed to the job
    /// submitter the train is routed to, skipping the work queue. The train's own authorization
    /// applies, as for <see cref="QueueTrain"/>.
    /// </summary>
    /// <remarks>
    /// A refusal is <c>success: false</c> with a message and writes nothing: an unknown train,
    /// invalid or oversized input, or a refusal the train itself makes. A caller who may not run
    /// the train gets the <c>TRAX_AUTHORIZATION</c> GraphQL error. A failure to submit the run,
    /// which marks its row Failed, is a masked GraphQL error, as any server failure is.
    /// On success <c>id</c> is the execution's metadata id, not a work queue id.
    /// </remarks>
    public async Task<OperationResponse> RunTrain(
        RunTrainInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.RunTrainAsync(input, ct));

    /// <summary>
    /// Cancels a queued work queue entry. Only entries with status <c>Queued</c> can be
    /// cancelled.
    /// </summary>
    public async Task<OperationResponse> CancelWorkQueueEntry(
        long id,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.CancelWorkQueueEntryAsync(id, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Cancels many queued entries in one statement. Only entries still in <c>Queued</c> are
    /// affected; already-dispatched or cancelled ids are skipped. <c>count</c> is the number
    /// actually cancelled, zero included. An empty list, or more than 1000 ids, returns
    /// <c>success: false</c> and cancels nothing.
    /// </summary>
    public async Task<OperationResponse> CancelWorkQueueEntries(
        long[] ids,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.CancelWorkQueueEntriesAsync(ids, ct));

    private static OperationResponse ToResponse(OperationResult result) =>
        new(result.Success, result.Count, result.Message) { Id = result.Id };
}
