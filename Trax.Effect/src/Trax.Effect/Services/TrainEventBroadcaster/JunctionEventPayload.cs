using System.Text.Json.Serialization;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// The junction-level part of a <see cref="TrainLifecycleEventMessage"/> whose
/// <see cref="TrainLifecycleEventMessage.EventType"/> is one of the junction event types
/// (<see cref="TrainLifecycleEventMessage.IsJunctionEvent"/>): one step of a run, a junction that
/// started or ended, a question a routing step asked, or the track it took.
/// </summary>
/// <remarks>
/// <para>It carries names, times, states and how a failure is classified, and nothing a run was
/// given or produced: never a junction's input or output, the train's input or output, a failure's
/// message, or the state a decider was shown. A question's answer is summarised as the option, score
/// or probability the run acted on, and is left out, with <see cref="AnswerWithheld"/> set, when the
/// question is about a type marked <c>[TraxSensitive]</c>.</para>
///
/// <para>Published only by a host that called <c>AddJunctionEvents()</c>. The same values are stored
/// in <c>trax.junction_run</c>, one row per <see cref="Position"/>.</para>
/// </remarks>
/// <param name="Position">
/// Where the step falls in the run, from 0. A junction's start and end carry the same position, and
/// ordering by it gives the run's timeline.
/// </param>
/// <param name="Kind">What the step is.</param>
/// <param name="Name">
/// The junction's class name without its namespace, or for a question or a track, the question's key.
/// </param>
/// <param name="State">Where the step stands after this event.</param>
/// <param name="StartedAt">When the junction started, or when the question was answered or the track taken (UTC).</param>
/// <param name="EndedAt">When the junction returned (UTC), or null for a start.</param>
/// <param name="DurationMs">
/// Milliseconds from <paramref name="StartedAt"/> to <paramref name="EndedAt"/>, or null for a start.
/// </param>
/// <param name="FailureClass">
/// How a failed junction's failure is classified, decided as the run's own failure would be; null
/// unless the junction failed.
/// </param>
/// <param name="FailureException">
/// The type name of the exception a junction failed or was cancelled with, never its message.
/// </param>
/// <param name="QuestionKey">The question's key, for a question or a track.</param>
/// <param name="Answer">
/// The answer the run acted on: an option's name, a score or a probability of yes (invariant
/// culture, round-trip format), or for a track, the track's name. Null for a junction, a refused
/// answer, and a withheld one.
/// </param>
/// <param name="Confidence">How sure the decider was, for a choice or a score.</param>
/// <param name="Replayed">True when the answer came from an earlier run rather than a decider.</param>
/// <param name="Decider">The full name of the decider's type, or null when the answer was replayed.</param>
/// <param name="AnswerWithheld">
/// True when the question is about a type marked <c>[TraxSensitive]</c>, so its answer, confidence
/// and track are left out.
/// </param>
/// <param name="NameWithheld">
/// True for a step (a junction, a question or a routing step) that runs after a routing step whose
/// answer is withheld (a question about a type marked <c>[TraxSensitive]</c>): its
/// <paramref name="Name"/> is <see cref="WithheldName"/>, and a question's or routing step's
/// <paramref name="QuestionKey"/>, <paramref name="Answer"/>, <paramref name="Confidence"/> and
/// <paramref name="Decider"/> are left out, because which steps ran would give the track, and so
/// the answer, away.
/// </param>
/// <param name="TrackPosition">
/// For any step, the <paramref name="Position"/> of the latest routing step (a <c>Route</c>) the run
/// took before it, or null when no routing step has run yet. Trax.Core reports where a track
/// starts but not where it rejoins the chain, so every junction after a route is counted as on its
/// track. A subscriber that is not shown answers should not be shown these steps' names or keys
/// either.
/// </param>
/// <param name="Attempt">
/// Which attempt of its manifest the run is: 1 plus the number of the manifest's failed runs since
/// its last completed or cancelled run (a dispatch attempt the scheduler requeued does not count),
/// worked out once when the run begins. Null for a run with no manifest, and when it could not be
/// worked out.
/// </param>
public sealed record JunctionEventPayload(
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("kind")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<JunctionRunKind>))]
        JunctionRunKind Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<JunctionRunState>))]
        JunctionRunState State,
    [property: JsonPropertyName("startedAt")] DateTime StartedAt,
    [property: JsonPropertyName("endedAt")] DateTime? EndedAt = null,
    [property: JsonPropertyName("durationMs")] double? DurationMs = null,
    [property: JsonPropertyName("failureClass")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<FailureClass>))]
        FailureClass? FailureClass = null,
    [property: JsonPropertyName("failureException")] string? FailureException = null,
    [property: JsonPropertyName("questionKey")] string? QuestionKey = null,
    [property: JsonPropertyName("answer")] string? Answer = null,
    [property: JsonPropertyName("confidence")] double? Confidence = null,
    [property: JsonPropertyName("replayed")] bool Replayed = false,
    [property: JsonPropertyName("decider")] string? Decider = null,
    [property: JsonPropertyName("answerWithheld")] bool AnswerWithheld = false,
    [property: JsonPropertyName("attempt")] int? Attempt = null,
    [property: JsonPropertyName("nameWithheld")] bool NameWithheld = false,
    [property: JsonPropertyName("trackPosition")] int? TrackPosition = null
)
{
    /// <summary>The <see cref="Name"/> of a junction whose name is withheld.</summary>
    public const string WithheldName = "(withheld)";

    /// <summary>
    /// The id of the declared node the step ran for, as <c>ChainGraph.From</c> draws it for the
    /// train's <c>DeclaredChain()</c>, such as <c>Switch&lt;Source&gt;#0/Papers/FetchPapers#0</c>,
    /// so a subscriber can place the step on the train's graph. Null when
    /// <see cref="NameWithheld"/> is set, because the id names the track and the junction.
    /// </summary>
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    /// <summary>
    /// The <c>Parallel</c> branch the step ran in, as Trax.Core names it, such as
    /// <c>Parallel#0/cocitation</c>, so a subscriber can draw the steps of branches that ran side
    /// by side in a lane each. Null for a step outside any branch, and when
    /// <see cref="NameWithheld"/> is set, because a branch path can name a track.
    /// </summary>
    [JsonPropertyName("branchPath")]
    public string? BranchPath { get; init; }
}
