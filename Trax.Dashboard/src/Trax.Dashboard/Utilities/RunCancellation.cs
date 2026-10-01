using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Cancels one run through <see cref="IOperationsService.CancelExecutionsAsync"/>, the call the
/// GraphQL <c>cancelExecution</c> mutation makes, and answers the way that mutation does: a run
/// that is missing or already finished is a failure, not a success with nothing flagged.
/// </summary>
internal static class RunCancellation
{
    public static async Task<OperationResult> CancelOneAsync(
        IOperationsService operations,
        long metadataId,
        CancellationToken ct
    )
    {
        var result = await operations.CancelExecutionsAsync([metadataId], ct);
        if (!result.Success || result.Count > 0)
            return result;

        return new OperationResult(
            false,
            Count: 0,
            Message: $"Execution {metadataId} is not cancellable (missing or already terminal)."
        );
    }
}
