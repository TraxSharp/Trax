namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Result of the <c>stateMachine.sendSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set: refusals come back as data, never as a GraphQL error.
/// </summary>
public record SendSnapshotOutput
{
    /// <summary>The draft after the effect ran and the machine advanced (or the replayed result of an earlier send), in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Why the send was refused or failed (for example <c>NO_EFFECT</c>, <c>NOT_FOUND</c>, <c>REQUEST_ID_REUSED</c>, or <c>DELIVERY_FAILED</c> when the effect threw and the draft was not advanced, so the send can be retried); null on success.</summary>
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

    /// <summary>
    /// False when this answer replays a request the instance has moved past since, whose due time the server cannot
    /// know: <see cref="DueAt"/> is then null and says nothing. True on every other answer, where a null
    /// <see cref="DueAt"/> means the state has no timer or the server cleared it.
    /// </summary>
    public bool DueAtKnown { get; init; } = true;
}
