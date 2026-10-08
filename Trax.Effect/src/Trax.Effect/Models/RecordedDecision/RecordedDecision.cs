using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.RecordedDecision;

/// <summary>
/// Base model for <c>trax.decision</c>: one question a train asked a decider during a run, the
/// answer it acted on, and the tracks taken on it.
/// </summary>
/// <remarks>
/// Written by <c>AddDecisionRecording</c> as each decision is made, before the run acts on it, so
/// a run that dies mid-way leaves what it decided, and so is a decider's answer the run refused,
/// with the reason in <see cref="Refused"/>; each track is added to <see cref="Routes"/> when
/// a routing step takes one. Read back to replay a run's decisions when it is requeued. EF Core mapping lives in
/// <c>Trax.Effect.Data.Models.RecordedDecision.PersistentRecordedDecision</c>; the table
/// ships in the core migration set (Postgres <c>054</c> and <c>068</c>, Sqlite <c>019</c> and <c>031</c>) and is deleted with
/// its run.
/// </remarks>
public class RecordedDecision
{
    /// <summary>The row's identity.</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>The run that asked.</summary>
    [Column("metadata_id")]
    public long MetadataId { get; set; }

    /// <summary>
    /// The question's key, as <c>QuestionKey.For</c> gives it: the <c>[Asks(Key = ...)]</c> on the
    /// enum or marker type it is about, or else that type's name without its namespace.
    /// </summary>
    [Column("question_key")]
    public string QuestionKey { get; set; } = null!;

    /// <summary>
    /// The <c>Parallel</c> branch that asked, as Trax.Core names it (<c>Parallel#0/cocitation</c>),
    /// or empty for a question asked outside any branch. A branch counts its askings on from where
    /// it forked, so two branches asking one question ask it under the same
    /// <see cref="Occurrence"/>; an asking is told apart by its branch as well.
    /// </summary>
    [Column("branch_path")]
    public string BranchPath { get; set; } = "";

    /// <summary>
    /// Which asking of the question this was in the run, from 0. Inside a <c>Parallel</c> branch
    /// it counts on from where the branch forked.
    /// </summary>
    [Column("occurrence")]
    public int Occurrence { get; set; }

    /// <summary><c>choice</c>, <c>score</c> or <c>yes_no</c>.</summary>
    [Column("kind")]
    public string Kind { get; set; } = null!;

    /// <summary>The question as asked, with its instructions and criteria, as JSON.</summary>
    [Column("question")]
    public string Question { get; set; } = null!;

    /// <summary>
    /// The answer the run acted on, as JSON. Carries <c>replay_refused</c> when an earlier run's
    /// answer to the question no longer fitted it and the decider was asked afresh. For a
    /// <see cref="Refused"/> row it is the answer the run would not act on, or null when the
    /// decider gave none.
    /// </summary>
    [Column("answer")]
    public string? Answer { get; set; }

    /// <summary>
    /// Why the run would not act on the decider's answer, or null for an answer it acted on. A
    /// refused row is the last thing its run decided: the step failed on it, and it is never
    /// replayed, so a requeue of the run asks the question afresh.
    /// </summary>
    [Column("refused")]
    public string? Refused { get; set; }

    /// <summary>
    /// Identifies the asking the answer was given to, as Trax.Core computed it: 64 lowercase hex
    /// characters over the step, the state's type and the question's declaration. Handed back with
    /// the answer on replay, and an answer whose fingerprint differs from the asking it would be
    /// replayed into is not acted on.
    /// </summary>
    [Column("fingerprint")]
    public string Fingerprint { get; set; } = null!;

    /// <summary>
    /// The model that answered, as the decider named it (for a System One model, the name the
    /// request asked for, echoed back), or null for a decider that is not a model.
    /// </summary>
    [Column("model")]
    public string? Model { get; set; }

    /// <summary>The decider's type, or null when the answer was replayed.</summary>
    [Column("decider")]
    public string? Decider { get; set; }

    /// <summary>True when the answer came from an earlier run rather than a decider.</summary>
    [Column("replayed")]
    public bool Replayed { get; set; }

    /// <summary>What each shadow decider answered and whether it agreed, as JSON, or null.</summary>
    [Column("shadows")]
    public string? Shadows { get; set; }

    /// <summary>
    /// The tracks routing steps took on this decision, in the order they took them, as a JSON
    /// array of <c>{"track": ..., "fallback_reason": ...}</c>, or null when nothing routed on it.
    /// </summary>
    /// <remarks>
    /// More than one step can route on one decision (a <c>Decide</c> followed by two
    /// <c>Switch</c> steps on the same choice), so each routing is kept rather than the last one
    /// replacing the first. <c>fallback_reason</c> says why the decision was not followed, and is
    /// null when it was.
    /// </remarks>
    [Column("routes")]
    public string? Routes { get; set; }

    /// <summary>
    /// The hash of the state the question was asked about, as Trax.Core computed it: <c>k1:</c> and
    /// 64 lowercase hex characters of an HMAC-SHA256 when a state hash key is configured, otherwise
    /// <c>s1:</c> and 64 of a plain SHA-256. A replay hands it back, and Trax.Core replays the
    /// answer only into a state that hashes the same; a null one is never replayed.
    /// </summary>
    /// <remarks>
    /// Null when Trax.Core could not encode the state, when the row predates the column, and when
    /// no key is configured and the state can hold a value marked <c>[TraxSensitive]</c> or the
    /// question is about a type so marked, because an unkeyed hash of such a state can be checked
    /// against guessed values by anyone who reads it.
    /// </remarks>
    [Column("state_hash")]
    public string? StateHash { get; set; }

    /// <summary>When the question was answered.</summary>
    [Column("decided_at")]
    public DateTime DecidedAt { get; set; }
}
