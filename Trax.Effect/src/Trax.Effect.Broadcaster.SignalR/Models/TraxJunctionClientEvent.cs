using System.Text.Json.Serialization;

namespace Trax.Effect.Broadcaster.SignalR.Models;

/// <summary>
/// The payload sent to SignalR clients through the <c>"JunctionEvent"</c> client method: one step
/// of a run, from a host that called <c>AddJunctionEvents()</c>, when the sink was configured with
/// <c>WithJunctionEvents()</c>.
/// </summary>
/// <remarks>
/// It carries the step's name, position, kind, state and times, a failed junction's failure class
/// and exception type, and for a question its key. The answer and the confidence are left null unless
/// the sink was configured with <c>WithJunctionAnswers()</c>, and stay null for a question whose
/// answer is withheld. Never an input, an output, a failure's message or anything a decider was
/// shown. A junction that runs on a decision's track (<c>trackPosition</c> set) has its name
/// withheld too unless the sink sends answers, and always when its track's answer is withheld,
/// because which junctions ran would give the answer away. A null is left off the wire. Field
/// meanings are those of <c>JunctionEventPayload</c>.
/// </remarks>
public record TraxJunctionClientEvent(
    [property: JsonPropertyName("metadataId")] long MetadataId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("startedAt")] DateTime StartedAt,
    [property: JsonPropertyName("endedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        DateTime? EndedAt,
    [property: JsonPropertyName("durationMs")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double? DurationMs,
    [property: JsonPropertyName("failureClass")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? FailureClass,
    [property: JsonPropertyName("failureException")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? FailureException,
    [property: JsonPropertyName("questionKey")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? QuestionKey,
    [property: JsonPropertyName("answer")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Answer,
    [property: JsonPropertyName("confidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double? Confidence,
    [property: JsonPropertyName("replayed")] bool Replayed,
    [property: JsonPropertyName("answerWithheld")] bool AnswerWithheld,
    [property: JsonPropertyName("nameWithheld")] bool NameWithheld,
    [property: JsonPropertyName("trackPosition")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? TrackPosition
)
{
    /// <summary>
    /// The id of the declared node the step ran for, as <c>ChainGraph</c> draws it. Left off, like
    /// the name, for a step whose name is withheld, because the id names the track and the step.
    /// </summary>
    [JsonPropertyName("nodeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }
}
