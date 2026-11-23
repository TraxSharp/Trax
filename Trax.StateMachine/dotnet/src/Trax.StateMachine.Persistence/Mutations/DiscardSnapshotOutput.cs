namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Result of the <c>stateMachine.discardSnapshot</c> mutation. Either <see cref="Discarded"/> is true or
/// <see cref="Problem"/> is set.
/// </summary>
public record DiscardSnapshotOutput
{
    /// <summary>
    /// True when the instance is gone, whether this request deleted it or it was already gone: a discard is
    /// idempotent, and another user's id answers exactly as a missing one does.
    /// </summary>
    public bool Discarded { get; init; }

    /// <summary>
    /// Why the discard was refused (for example <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>,
    /// <c>INVALID_REQUEST_ID</c> or <c>SYSTEM_OWNED</c>); null on success. The instance is unchanged when it is set.
    /// </summary>
    public SnapshotProblem? Problem { get; init; }
}
