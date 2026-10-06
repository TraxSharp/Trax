namespace Trax.Api.DTOs;

/// <summary>
/// The result of an operations mutation. A mutation that could not do what was asked (the target
/// does not exist, is in the wrong state, or the request was refused) returns
/// <c>Success = false</c> with a <see cref="Message"/> rather than a GraphQL error. A failure of
/// the server itself, or a caller not allowed to run the train, is a GraphQL error instead.
/// </summary>
/// <param name="Success">Whether the operation did what was asked.</param>
/// <param name="Count">How many items the operation affected, for operations that act on a set; <c>null</c> otherwise.</param>
/// <param name="Message">A human-readable description of the outcome.</param>
public record OperationResponse(bool Success, int? Count = null, string? Message = null)
{
    /// <summary>
    /// The id of the one row the operation acted on, when there is one: the work queue entry
    /// <c>queueTrain</c> and <c>requeueExecution</c> created, the execution (metadata) id
    /// <c>runTrain</c> started, the work queue entry a manifest trigger queued or released, or the
    /// manifest or group <c>updateManifest</c> or <c>updateManifestGroup</c> patched. <c>null</c>
    /// otherwise, and on a failure that touched no row.
    /// </summary>
    public long? Id { get; init; }
}
