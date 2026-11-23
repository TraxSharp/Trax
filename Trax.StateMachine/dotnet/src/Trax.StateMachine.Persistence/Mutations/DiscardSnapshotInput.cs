namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Input of the <c>stateMachine.discardSnapshot</c> mutation (<see cref="DiscardSnapshot"/>): delete one of the
/// caller's own instances, cancelling a run its state invoked first.
/// </summary>
public record DiscardSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>UNKNOWN_MACHINE</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The instance id, scoped to the authenticated user: another user's id answers as a missing one.</summary>
    public required Guid Id { get; init; }

    /// <summary>The idempotency key of this discard, required.</summary>
    public required string RequestId { get; init; }
}
