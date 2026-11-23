namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Result of the <c>stateMachine.saveSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set: refusals come back as data, never as a GraphQL error.
/// </summary>
public record SaveSnapshotOutput
{
    /// <summary>The snapshot as stored, in the machine's canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Why the save was refused (for example <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, <c>SCHEMA_MISMATCH</c>, <c>TOO_LARGE</c>, <c>DRAFT_COMMITTED</c>, <c>CONFLICT</c> or a rehydration error code); null on success. Nothing was written when it is set.</summary>
    public SnapshotProblem? Problem { get; init; }

    /// <summary>
    /// The instance's revision after this request, bumped by the database on every write, or <see langword="null"/> where the
    /// store does not record one. It travels beside <see cref="Snapshot"/>, never inside it, and orders the writes
    /// of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one. <see langword="null"/> where the store does not record one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <see langword="null"/> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}
