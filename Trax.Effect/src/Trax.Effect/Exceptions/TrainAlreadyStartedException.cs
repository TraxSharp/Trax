using Trax.Core.Exceptions;

namespace Trax.Effect.Exceptions;

/// <summary>
/// Thrown when a run is asked to start from a pre-created row that another execution has already
/// moved out of <c>Pending</c>. The train's body has not run, and nothing about the row was
/// changed by this attempt.
/// </summary>
/// <remarks>
/// A queue that delivers at least once, an HTTP dispatch that is retried after the first attempt
/// was accepted, or a job claimed twice can hand the same row to two executions. Each holds a copy
/// that says <c>Pending</c>, so the in-memory check passes for both; the start is claimed in the
/// store, and the execution that loses the claim is refused with this exception. The row belongs
/// to the execution that won: a caller catching this must not record anything on it.
/// </remarks>
/// <param name="metadataId">The id of the row that was already started.</param>
/// <param name="trainName">The train the row belongs to.</param>
public class TrainAlreadyStartedException(long metadataId, string trainName)
    : TrainException(
        $"Metadata ({metadataId}) for train ({trainName}) is no longer Pending: another execution has already started it."
    )
{
    /// <summary>The id of the row that was already started.</summary>
    public long MetadataId { get; } = metadataId;

    /// <summary>The train the row belongs to.</summary>
    public string TrainName { get; } = trainName;
}
