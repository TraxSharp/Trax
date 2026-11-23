using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Advance(string, Guid, string, JsonNode, string, CancellationToken)"/>.</summary>
public abstract record AdvanceOutcome
{
    /// <summary>
    /// The trigger fired and the result was persisted, or the request was a replay of the one the draft
    /// last recorded and nothing was fired again.
    /// </summary>
    /// <param name="Snapshot">The draft's snapshot after the advance (or the current one, on a replay).</param>
    public sealed record Advanced(Snapshot Snapshot) : AdvanceOutcome
    {
        /// <summary>
        /// The revision this advance left the instance at: the revision it read plus one, since the write is guarded
        /// by the token read with it, so it is reported without reading the row back. On a replay, the revision the
        /// instance is at. <see langword="null"/> where the store does not record one. It travels beside the snapshot, never
        /// inside it.
        /// </summary>
        public long? Revision { get; init; }

        /// <summary>
        /// The row's surrogate key, which names one incarnation of the instance, or <see langword="null"/> where the store does
        /// not record one.
        /// </summary>
        public long? RowId { get; init; }

        /// <summary>
        /// When the timer of the state the instance is in fires, or <see langword="null"/> when that state has no timer, the
        /// server cleared it, or it is not recorded yet. Meaningful only while <see cref="DueAtKnown"/> is true.
        /// </summary>
        public DateTimeOffset? DueAt { get; init; }

        /// <summary>
        /// False when this answer is a replay of a request the instance has moved past since: the ring records the
        /// result, not its due time, so <see cref="DueAt"/> is null and says nothing about the replayed state's
        /// timer. True on every other answer, where a null <see cref="DueAt"/> means there is no timer.
        /// </summary>
        public bool DueAtKnown { get; init; } = true;
    }

    /// <summary>The advance was refused and the stored draft is unchanged.</summary>
    /// <param name="Reason">
    /// A machine rejection reason (see <see cref="RefusalCode"/>), or one of the service's own:
    /// <c>EFFECT_BOUND</c> (the trigger runs the machine's irreversible effect from this state, so only
    /// <see cref="ISnapshotEffectRunner.Run"/> may fire it), <c>REQUEST_ID_REUSED</c>, <c>TOO_LARGE</c>,
    /// <c>DRAFT_CHANGED</c> (from <see cref="ISnapshotEffectRunner.Run"/> only: the effect already ran on content
    /// the draft no longer holds, so its receipt is not recorded and the effect does not run again),
    /// <c>CLIENT_DIVERGENCE</c>, or <see cref="RefusalCode.Malformed"/> when the resulting context
    /// could not be stored.
    /// </param>
    /// <param name="Detail">Extra context for the reason, such as the failing guard; null when there is none.</param>
    public sealed record Rejected(RefusalCode Reason, string? Detail) : AdvanceOutcome
    {
        /// <summary>The exception behind an <c>INTERNAL_ERROR</c>, kept for the server's log and never in <see cref="Detail"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>No draft exists for this user and id, so there is nothing to advance.</summary>
    public sealed record NotFound : AdvanceOutcome;

    /// <summary>The stored draft failed rehydration, so it could not be advanced; it is left untouched.</summary>
    /// <param name="Code">A rehydration error code, one of <see cref="RefusalCode"/>.</param>
    /// <param name="Message">A human-readable explanation of what failed validation.</param>
    public sealed record LoadError(RefusalCode Code, string Message) : AdvanceOutcome
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>Another writer advanced this draft first — the client's view is stale.</summary>
    public sealed record Conflict : AdvanceOutcome;

    private AdvanceOutcome() { }
}
