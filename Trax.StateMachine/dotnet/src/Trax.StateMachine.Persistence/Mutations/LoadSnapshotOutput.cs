namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Result of the <c>stateMachine.loadSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set.
/// </summary>
public record LoadSnapshotOutput
{
    /// <summary>The stored draft in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Set when there is no such draft (normal: start fresh) or the stored data failed validation.</summary>
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
