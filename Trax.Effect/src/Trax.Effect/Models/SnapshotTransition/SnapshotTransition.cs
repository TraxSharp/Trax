using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Enums;

namespace Trax.Effect.Models.SnapshotTransition;

/// <summary>
/// Base model for <c>trax.snapshot_transition</c>: one write of a state-machine instance's snapshot, in its history.
/// </summary>
/// <remarks>
/// <para>Trax.Effect.StateMachine.Persistence's store writes one row for every snapshot it writes, in the same
/// transaction, with the revision that write gave the instance: its creation, every command, save, timer and outcome.
/// A write that leaves the snapshot alone (marking a pending effect, setting a run's token, keeping a draft active)
/// writes none, and neither does a writer that goes around the store, so revisions can skip.</para>
///
/// <para>A row holds names and times only, never the instance's context or its owner's key. It belongs to one
/// incarnation of the instance (<see cref="DraftRowId"/>) and is deleted with it, by the database, however the
/// instance goes: discarded, expired or deleted by host code.</para>
///
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.SnapshotTransition.PersistentSnapshotTransition</c>; the table
/// ships in the core migration set (Postgres <c>087</c>, Sqlite <c>046</c>). See
/// <c>Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </remarks>
public class SnapshotTransition
{
    /// <summary>The row's identity.</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>The instance's row (<c>trax.snapshot_draft.row_id</c>), which names one incarnation of it.</summary>
    [Column("draft_row_id")]
    public long DraftRowId { get; set; }

    /// <summary>The revision the write gave the instance. Unique within the row.</summary>
    [Column("revision")]
    public long Revision { get; set; }

    /// <summary>What wrote it.</summary>
    [Column("kind")]
    public SnapshotTransitionKind Kind { get; set; }

    /// <summary>
    /// The command that moved it (a machine event's name, a timer's event, or <c>State.done</c>, <c>State.failed</c>
    /// or <c>State.cancelled</c> for a run's outcome), or null for a creation, a save, or an outcome that could not be
    /// applied.
    /// </summary>
    [Column("trigger")]
    public string? Trigger { get; set; }

    /// <summary>Who made it.</summary>
    [Column("actor")]
    public SnapshotTransitionActor Actor { get; set; }

    /// <summary>The state it left, or null for its creation.</summary>
    [Column("from_state")]
    public string? FromState { get; set; }

    /// <summary>The state it entered, which is <see cref="FromState"/> again when it stayed.</summary>
    [Column("to_state")]
    public string ToState { get; set; } = null!;

    /// <summary>When it was written: the instance's <c>updated_at</c> from the same write.</summary>
    [Column("occurred_at")]
    public DateTimeOffset At { get; set; }

    /// <summary>
    /// The token of what the write started, or null when it started nothing: the external id of the train run that
    /// entering its state queued, or the token a child machine it spawned shares with it. Server-only: no operator
    /// surface returns it; it links the row to the run or the child.
    /// </summary>
    [Column("invoke_token")]
    public string? InvokeToken { get; set; }
}
