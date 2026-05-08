using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations for the work queue: queue a train for execution and cancel queued entries.
/// Thin wrappers around <see cref="IOperationsService"/>; the dashboard UI calls the same
/// service directly so both surfaces share validation and persistence.
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

    private static OperationResponse ToResponse(OperationResult result) =>
        new(result.Success, result.Count, result.Message);
}
