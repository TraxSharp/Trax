namespace Trax.StateMachine.Persistence;

/// <summary>One client request a row recorded, with the result it was answered with.</summary>
/// <param name="RequestId">The client's idempotency key.</param>
/// <param name="Fingerprint">
/// A fingerprint of the request (its trigger and its input), so the same key with a different request is told apart.
/// </param>
/// <param name="Revision">The revision the request produced, or 0 from a store that records none.</param>
/// <param name="ResultSnapshot">The snapshot the request was answered with, in canonical wire JSON.</param>
public sealed record RecordedRequest(
    string RequestId,
    string Fingerprint,
    long Revision,
    string ResultSnapshot
)
{
    /// <summary>The trigger the request fired.</summary>
    public string? Trigger { get; init; }

    /// <summary>The state the request fired from.</summary>
    public string? FromState { get; init; }
}
