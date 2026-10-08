using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.Checkpoint;

/// <summary>
/// Base model for <c>trax.checkpoint</c>: the state a train declared with <c>Checkpoint&lt;TState&gt;()</c>,
/// stored when its run reached that step, so a later run of the same input can resume there instead of
/// running every step before it again.
/// </summary>
/// <remarks>
/// One row per run and declared node. Trax never stores a run's Memory: the row holds the declared
/// <c>TState</c> and the tracks the routing steps before it took, nothing else. A resume refuses a row
/// whose <see cref="ChainHash"/> or <see cref="StateFingerprint"/> differs from the running code.
///
/// <para>The row is run data. No operator surface returns <see cref="State"/> or <see cref="Tracks"/>;
/// operators see that a run has a checkpoint and at which node.</para>
///
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.Checkpoint.PersistentCheckpoint</c>; the table
/// ships in the core migration set (Postgres <c>074</c>, Sqlite <c>036</c>) and is deleted with its run.
/// A run that completes deletes its own rows, since nothing may resume it. See
/// <c>Trax.Docs/adr/0047</c>.
/// </remarks>
public class Checkpoint
{
    /// <summary>The row's identity.</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>The run that reached the checkpoint.</summary>
    [Column("metadata_id")]
    public long MetadataId { get; set; }

    /// <summary>
    /// The declared node's id, as Trax.Core numbers it: <c>Checkpoint&lt;CheckedFindings&gt;#0</c>, or
    /// <c>Parallel#0/embedding/Checkpoint&lt;EmbeddingScores&gt;#0</c> inside a branch, whose path is
    /// already part of the id. Unique within the run.
    /// </summary>
    [Column("node_id")]
    public string NodeId { get; set; } = null!;

    /// <summary>
    /// The <c>Parallel</c> branch the checkpoint was taken in (<c>Parallel#0/embedding</c>), or null
    /// outside any branch. A resume into a failed <c>Parallel</c> reruns each branch from its own latest
    /// checkpoint, found by this column.
    /// </summary>
    [Column("branch_path")]
    public string? BranchPath { get; set; }

    /// <summary>
    /// The declared state's type, readable, for display and for error messages only. A resume matches a
    /// row by <see cref="NodeId"/> and checks it by <see cref="StateFingerprint"/>, never by this name.
    /// </summary>
    [Column("state_type")]
    public string StateType { get; set; } = null!;

    /// <summary>
    /// The declared state as canonical JSON, under the size cap a requeue's stored input uses. Never a
    /// placeholder: a state over the cap fails the step and stores no row.
    /// </summary>
    [Column("state")]
    public string State { get; set; } = null!;

    /// <summary>
    /// The tracks the routing steps before the checkpoint took, in order, as a JSON array; empty when
    /// none routed. A resume takes these tracks rather than routing again. A track whose routing type is
    /// marked <c>[TraxSensitive]</c> is never stored.
    /// </summary>
    [Column("tracks")]
    public string Tracks { get; set; } = "[]";

    /// <summary>
    /// The chain's hash when the row was written (<c>ChainGraph.Hash</c>, 64 lowercase hex characters
    /// over the declared graph's names, types and tracks). A resume refuses a row whose hash differs
    /// from the running chain's.
    /// </summary>
    [Column("chain_hash")]
    public string ChainHash { get; set; } = null!;

    /// <summary>
    /// A fingerprint of the state type's serializer contract when the row was written. A resume refuses
    /// a row whose fingerprint differs from the running type's, since its JSON would read back as a
    /// different value.
    /// </summary>
    [Column("state_fingerprint")]
    public string StateFingerprint { get; set; } = null!;

    /// <summary>When the checkpoint was written.</summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
