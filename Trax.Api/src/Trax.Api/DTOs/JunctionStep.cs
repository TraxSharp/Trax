using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.DTOs;

/// <summary>
/// One step of a run: a junction that ran, a question a routing step asked, or the track it took.
/// The <c>onJunctionEvent</c> subscription carries one per event, and
/// <c>operations.junctionRuns</c> returns a run's steps as <c>AddJunctionEvents()</c> recorded them.
/// </summary>
/// <remarks>
/// It carries names, times, states and how a failure is classified, and never anything the run was
/// given or produced: no input, no output, no failure message. An answer to a question about a type
/// marked <c>[TraxSensitive]</c> is never present (<see cref="AnswerWithheld"/>).
/// </remarks>
/// <param name="Position">Where the step falls in the run, from 0. A junction's start and end share it.</param>
/// <param name="Kind">What the step is.</param>
/// <param name="Name">The junction's class name without its namespace, or a question's key.</param>
/// <param name="State">Where the step stands.</param>
/// <param name="StartedAt">When the junction started, or when the question was answered or the track taken (UTC).</param>
/// <param name="EndedAt">When the junction returned (UTC), or <c>null</c> while it runs.</param>
/// <param name="DurationMs">Milliseconds from <paramref name="StartedAt"/> to <paramref name="EndedAt"/>, or <c>null</c> while it runs.</param>
/// <param name="FailureClass">How a failed junction's failure is classified; <c>null</c> unless it failed.</param>
/// <param name="FailureException">The type name of the exception a junction failed or was cancelled with, never its message.</param>
/// <param name="QuestionKey">The question's key, for a question or a track.</param>
/// <param name="Answer">The answer the run acted on, or <c>null</c> for a junction, a refused answer and a withheld one.</param>
/// <param name="Confidence">How sure the decider was, for a choice or a score; <c>null</c> when withheld.</param>
/// <param name="Replayed">True when the answer came from an earlier run rather than a decider.</param>
/// <param name="Decider">The full name of the decider's type. Live events only; the recorded timeline does not keep it.</param>
/// <param name="AnswerWithheld">True when the question is about a <c>[TraxSensitive]</c> type, so its answer and confidence are absent.</param>
/// <param name="Attempt">Which attempt of its manifest the run is, or <c>null</c> for a run with no manifest.</param>
/// <param name="NameWithheld">
/// True when <paramref name="Name"/> is <c>(withheld)</c>: the step is on a decision track, and its
/// name would give away an answer this caller is not shown.
/// </param>
/// <param name="TrackPosition">
/// The position of the latest routing step before this one, or <c>null</c> before any. Every step
/// after a route, a junction, a question or a further route, counts as on its track.
/// </param>
public sealed record JunctionStep(
    int Position,
    JunctionRunKind Kind,
    string Name,
    JunctionRunState State,
    DateTime StartedAt,
    DateTime? EndedAt,
    double? DurationMs,
    FailureClass? FailureClass,
    string? FailureException,
    string? QuestionKey,
    string? Answer,
    double? Confidence,
    bool Replayed,
    string? Decider,
    bool AnswerWithheld,
    int? Attempt,
    bool NameWithheld = false,
    int? TrackPosition = null
)
{
    /// <summary>
    /// The id of the declared node this step ran for, as the train's graph names it
    /// (<c>operations.declaredChain</c>), such as <c>Switch&lt;Source&gt;#0/Papers/FetchPapers#0</c>.
    /// A question carries the id of the <c>Decide</c> step that asked it, a route its routing
    /// step's. Null for a step recorded
    /// before node ids were, and for a step on a track whose answer is withheld, from this caller
    /// or from everyone.
    /// </summary>
    /// <remarks>
    /// An id names the track a step sits on, so it is withheld wherever the step's name is.
    /// </remarks>
    public string? NodeId { get; init; }

    /// <summary>What a withheld name reads as.</summary>
    internal const string WithheldName = JunctionEventPayload.WithheldName;

    /// <summary>This step with its name withheld, as a caller not shown the run's answers sees it.</summary>
    internal JunctionStep WithNameWithheld() =>
        this with
        {
            Name = WithheldName,
            NameWithheld = true,
            NodeId = null,
        };

    /// <summary>
    /// This step as a caller not shown the run's answers sees it on a decision track: its name,
    /// question key, answer and confidence withheld, since each of them can tell which track ran.
    /// </summary>
    internal JunctionStep WithTrackWithheld() =>
        WithNameWithheld() with
        {
            QuestionKey = null,
            Answer = null,
            Confidence = null,
        };

    /// <summary>
    /// Whether this step is on a decision track: any step, of any kind, after a routing step.
    /// </summary>
    internal bool OnATrack => TrackPosition is not null;

    /// <summary>The step a live junction event carries.</summary>
    /// <param name="payload">The event's junction payload.</param>
    internal static JunctionStep From(JunctionEventPayload payload) =>
        new(
            payload.Position,
            payload.Kind,
            // A name the run withheld stays withheld whatever the payload holds.
            payload.NameWithheld
                ? WithheldName
                : payload.Name,
            payload.State,
            payload.StartedAt,
            payload.EndedAt,
            payload.DurationMs,
            payload.FailureClass,
            payload.FailureException,
            payload.QuestionKey,
            // A withheld answer stays absent whatever the payload holds.
            payload.AnswerWithheld
                ? null
                : payload.Answer,
            payload.AnswerWithheld ? null : payload.Confidence,
            payload.Replayed,
            payload.Decider,
            payload.AnswerWithheld,
            payload.Attempt,
            payload.NameWithheld,
            payload.TrackPosition
        )
        {
            // A withheld name keeps its node id withheld too: the id names the track.
            NodeId = payload.NameWithheld ? null : payload.NodeId,
        };

    /// <summary>The step a recorded <c>trax.junction_run</c> row holds.</summary>
    /// <param name="row">The row.</param>
    public static JunctionStep From(JunctionRun row) =>
        new(
            row.Position,
            row.Kind,
            row.NameWithheld ? WithheldName : row.Name,
            row.State,
            row.StartedAt,
            row.EndedAt,
            row.EndedAt is { } ended ? (ended - row.StartedAt).TotalMilliseconds : null,
            row.FailureClass,
            row.FailureException,
            row.QuestionKey,
            row.AnswerWithheld ? null : row.Answer,
            row.AnswerWithheld ? null : row.Confidence,
            row.Replayed,
            Decider: null,
            row.AnswerWithheld,
            row.Attempt,
            row.NameWithheld,
            row.TrackPosition
        )
        {
            // A withheld name keeps its node id withheld too: the id names the track.
            NodeId = row.NameWithheld ? null : row.NodeId,
        };
}
