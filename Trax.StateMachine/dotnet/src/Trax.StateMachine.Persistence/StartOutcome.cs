using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The total result of <see cref="ISnapshotDraftService.Start"/>.</summary>
public abstract record StartOutcome
{
    /// <summary>The instance was created, or the request was a retry of the start that created it.</summary>
    /// <param name="Id">The instance id the server minted.</param>
    /// <param name="Snapshot">The instance's snapshot.</param>
    /// <param name="Revision">The instance's revision.</param>
    /// <param name="Created">True when this request created the instance; false when it replayed an earlier start.</param>
    public sealed record Started(Guid Id, Snapshot Snapshot, long Revision, bool Created)
        : StartOutcome
    {
        /// <summary>
        /// When the timer of the state the instance is in fires, or <see langword="null"/> when that state has no timer (or the
        /// store does not record one). It travels beside the snapshot, never inside it.
        /// </summary>
        public DateTimeOffset? DueAt { get; init; }
    }

    /// <summary>The start was refused and nothing was written.</summary>
    /// <param name="Code">Why, for example one of <see cref="RefusalCode"/>.</param>
    /// <param name="Message">A human-readable explanation of the refusal.</param>
    public sealed record Rejected(RefusalCode Code, string Message) : StartOutcome;

    private StartOutcome() { }
}
