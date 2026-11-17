using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The total result of <see cref="ISnapshotDraftService.Load(string, Guid, CancellationToken)"/>.</summary>
public abstract record LoadResult
{
    /// <summary>The draft exists and rehydrated cleanly.</summary>
    /// <param name="Snapshot">The validated snapshot, at the machine's current definition version.</param>
    public sealed record Loaded(Snapshot Snapshot) : LoadResult
    {
        /// <summary>
        /// The instance's revision, bumped by every write, or <see langword="null"/> where the store does not record one. It
        /// travels beside the snapshot, never inside it.
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

        /// <summary>
        /// The concurrency token the draft was read with. The effect runner commits its receipt only while the draft
        /// still carries it, so the receipt is recorded on the content the effect ran on.
        /// </summary>
        internal Guid Token { get; init; }

        /// <summary>The effect pending on the draft's row when it was read, or null when none was.</summary>
        internal PendingEffect? Effect { get; init; }
    }

    /// <summary>
    /// No draft exists for this user and id, or it had been idle longer than the configured draft TTL and
    /// was deleted by this read. Treat it as "start fresh".
    /// </summary>
    public sealed record NotFound : LoadResult;

    /// <summary>A stored row exists but failed rehydration; the row is left untouched.</summary>
    /// <param name="Code">A rehydration error code, one of <see cref="RefusalCode"/>.</param>
    /// <param name="Message">A human-readable explanation of what failed validation.</param>
    public sealed record Invalid(RefusalCode Code, string Message) : LoadResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    private LoadResult() { }
}
