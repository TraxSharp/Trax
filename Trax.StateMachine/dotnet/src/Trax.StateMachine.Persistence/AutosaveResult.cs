using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Autosave"/>.</summary>
public abstract record AutosaveResult
{
    /// <summary>The client snapshot validated and was persisted.</summary>
    /// <param name="Snapshot">The snapshot as stored, after rehydration (and any version migration).</param>
    public sealed record Saved(Snapshot Snapshot) : AutosaveResult
    {
        /// <summary>
        /// The instance's revision as this save left it (the stored one when the save wrote nothing), or <see langword="null"/>
        /// where the store does not report one. It travels beside the snapshot, never inside it.
        /// </summary>
        public long? Revision { get; init; }

        /// <summary>
        /// The row's surrogate key, which names one incarnation of the instance, or <see langword="null"/> where the store does
        /// not record one.
        /// </summary>
        public long? RowId { get; init; }

        /// <summary>
        /// When the timer of the state the instance is in fires, or <see langword="null"/> when that state has no timer (or the
        /// store does not record one). It travels beside the snapshot, never inside it.
        /// </summary>
        public DateTimeOffset? DueAt { get; init; }
    }

    /// <summary>The snapshot was refused and nothing was written.</summary>
    /// <param name="Code">
    /// <c>TOO_LARGE</c> (over <see cref="SnapshotLimits.MaxSnapshotBytes"/>), <c>DRAFT_COMMITTED</c> (the stored
    /// draft is in a committed state or an effect's target and the save would change it in any way other than a
    /// reset to the initial state that the machine declares from that state), <c>STATE_RESERVED</c> (the save would create a draft in, or move one into, a committed state or
    /// the target of an effect-bound transition, which only the effect runner reaches), <c>DRAFT_UNREADABLE</c>
    /// (the stored draft fails rehydration, so only a reset to the initial state may overwrite it), or a
    /// <see cref="RefusalCode"/> value when the payload failed validation.
    /// </param>
    /// <param name="Message">A human-readable explanation of the refusal.</param>
    public sealed record Rejected(RefusalCode Code, string Message) : AutosaveResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>The draft changed elsewhere between read and write — reload and retry.</summary>
    public sealed record Conflict : AutosaveResult;

    private AutosaveResult() { }
}
