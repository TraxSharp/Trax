namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Result of the <c>stateMachine.startSnapshot</c> mutation. Either <see cref="Id"/> and <see cref="Snapshot"/> are
/// set, or <see cref="Problem"/> is: refusals come back as data, never as a GraphQL error.
/// </summary>
public record StartSnapshotOutput
{
    /// <summary>The id the server minted for the instance; null when <see cref="Problem"/> is set.</summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// True when this request created the instance, false when it was a retry of the start that did; null when
    /// <see cref="Problem"/> is set.
    /// </summary>
    public bool? Created { get; init; }

    /// <summary>The instance's snapshot in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>
    /// Why the start was refused (for example <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, <c>SCHEMA_MISMATCH</c>,
    /// <c>NO_START</c>, <c>FORBIDDEN</c>, <c>INVALID_REQUEST_ID</c>, <c>REQUEST_ID_REUSED</c>,
    /// <c>TOO_MANY_DRAFTS</c> or <c>INVALID_INPUT</c>); null on success. Nothing was written when it is set.
    /// </summary>
    public SnapshotProblem? Problem { get; init; }

    /// <summary>The instance's revision, or <see langword="null"/> when <see cref="Problem"/> is set.</summary>
    public long? Revision { get; init; }

    /// <summary>
    /// When the timer of the state the instance started in fires, or <see langword="null"/> when that state has no timer (or
    /// <see cref="Problem"/> is set). It travels beside <see cref="Snapshot"/>, never inside it.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}
