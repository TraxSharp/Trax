using System.ComponentModel.DataAnnotations.Schema;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Effect.Models.JunctionRun;

/// <summary>
/// Base model for <c>trax.junction_run</c>: one step of a run's timeline, a junction that ran or a
/// question a routing step asked and the track it took, in the order the run reached them.
/// </summary>
/// <remarks>
/// Written by <c>AddJunctionEvents</c> from the same events it publishes, so a finished run's
/// timeline, and the steps of a running one a late subscriber missed, can be read back. A junction's
/// row is written when it starts and updated when it ends. The rows are written off the run's path,
/// in order, by a background writer, so they can trail the live events by moments, and a row is
/// dropped rather than holding up the run when the writer falls behind or the database is down.
///
/// <para>A row never holds a junction's input or output, the train's input or output, or anything a
/// decider was shown: only names, times, states, how a failure is classified and the type of its
/// exception, and for a question the answer the run acted on. An answer to a question about a type
/// marked <c>[TraxSensitive]</c> is not stored at all (<see cref="AnswerWithheld"/>).</para>
///
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.JunctionRun.PersistentJunctionRun</c>; the
/// table ships in the core migration set (Postgres <c>055</c>, <c>057</c>, <c>060</c> and <c>067</c>, Sqlite <c>020</c>, <c>022</c>, <c>025</c> and <c>030</c>) and is deleted
/// with its run.
/// </remarks>
public class JunctionRun
{
    /// <summary>The row's identity.</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>The run the step belongs to.</summary>
    [Column("metadata_id")]
    public long MetadataId { get; set; }

    /// <summary>
    /// Where the step falls in the run, from 0: every junction start, question and track taken
    /// gets the next number, so ordering by it gives the timeline. Unique within the run.
    /// </summary>
    [Column("position")]
    public int Position { get; set; }

    /// <summary>What the step is.</summary>
    [Column("kind")]
    public JunctionRunKind Kind { get; set; }

    /// <summary>
    /// The junction's class name without its namespace, or for a question or a track, the
    /// question's key (<c>QuestionKey.For</c> of the type it is about).
    /// </summary>
    [Column("name")]
    public string Name { get; set; } = null!;

    /// <summary>Where the step stands.</summary>
    [Column("state")]
    public JunctionRunState State { get; set; }

    /// <summary>When the junction started, or when the question was answered or the track taken.</summary>
    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    /// <summary>When the junction returned, or null while it runs.</summary>
    [Column("ended_at")]
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// How a failed junction's failure is classified, as the run's own failure would be: the class
    /// the exception carries, else the registered <c>IFailureClassifier</c>'s, else
    /// <see cref="FailureClass.Transient"/> for a cancellation nothing asked for, else
    /// <see cref="FailureClass.Unclassified"/>. Null unless <see cref="State"/> is
    /// <see cref="JunctionRunState.Failed"/> for a junction.
    /// </summary>
    [Column("failure_class")]
    public FailureClass? FailureClass { get; set; }

    /// <summary>
    /// The type name of a failed junction's exception, such as <c>TrainException</c>. Never its
    /// message. Null unless the junction failed or was cancelled.
    /// </summary>
    [Column("failure_exception")]
    public string? FailureException { get; set; }

    /// <summary>The question's key, for a question or a track; null for a junction.</summary>
    [Column("question_key")]
    public string? QuestionKey { get; set; }

    /// <summary>
    /// The answer the run acted on: the chosen option's name for a <see cref="JunctionRunKind.Choice"/>,
    /// the score for a <see cref="JunctionRunKind.Score"/>, the probability of yes for a
    /// <see cref="JunctionRunKind.YesNo"/> (numbers in invariant culture, round-trip format), the
    /// track's name for a <see cref="JunctionRunKind.Route"/>. Null for a junction, for a refused
    /// answer, and when <see cref="AnswerWithheld"/> is set.
    /// </summary>
    [Column("answer")]
    public string? Answer { get; set; }

    /// <summary>
    /// How sure the decider was, from 0 to 1, for a choice or a score. Null otherwise, and when
    /// <see cref="AnswerWithheld"/> is set.
    /// </summary>
    [Column("confidence")]
    public double? Confidence { get; set; }

    /// <summary>True when the answer came from an earlier run rather than a decider.</summary>
    [Column("replayed")]
    public bool Replayed { get; set; }

    /// <summary>
    /// Which attempt of its manifest the run is, as junction events carry it; null for a run with
    /// no manifest, and when it could not be worked out.
    /// </summary>
    [Column("attempt")]
    public int? Attempt { get; set; }

    /// <summary>
    /// True for a junction that ran after a routing step whose answer is withheld: its
    /// <see cref="Name"/> is <c>(withheld)</c>, because which junctions ran would give the answer
    /// away.
    /// </summary>
    [Column("name_withheld")]
    public bool NameWithheld { get; set; }

    /// <summary>
    /// For a junction, the <see cref="Position"/> of the latest routing step the run took before it,
    /// or null before any. Every junction after a route is counted as on its track, because where
    /// tracks rejoin is not reported.
    /// </summary>
    [Column("track_position")]
    public int? TrackPosition { get; set; }

    /// <summary>
    /// True when the question is about a type marked <c>[TraxSensitive]</c>, so its answer,
    /// confidence and track are not stored.
    /// </summary>
    [Column("answer_withheld")]
    public bool AnswerWithheld { get; set; }

    /// <summary>
    /// The id of the declared node the step ran for, as <c>ChainGraph.From</c> draws it for the
    /// train's <c>DeclaredChain()</c>, such as <c>Switch&lt;Source&gt;#0/Papers/FetchPapers#0</c>.
    /// Null when <see cref="NameWithheld"/> is set, because the id names the track and the
    /// junction, and for rows written before the column existed.
    /// </summary>
    [Column("node_id")]
    public string? NodeId { get; set; }
}
