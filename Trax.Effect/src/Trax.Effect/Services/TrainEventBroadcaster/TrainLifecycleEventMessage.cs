using System.Text.Json.Serialization;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Serializable message representing a train lifecycle event for cross-process broadcasting.
/// This is the payload that flows through the message bus between worker and hub processes.
/// The same transport also carries coalesced data-change signals: those set
/// <see cref="EventType"/> to <see cref="DataChangedEventType"/> and put the changed domain in
/// <see cref="ChangeDomain"/>, leaving the train-specific fields empty. A host that called
/// <c>AddJunctionEvents()</c> also sends each step of a run over it: those carry one of the junction
/// event types (<see cref="IsJunctionEvent"/>) and the step in <see cref="Junction"/>, and reach only
/// <see cref="IJunctionEventHandler"/>s, never an <see cref="ITrainEventHandler"/>.
/// </summary>
public record TrainLifecycleEventMessage(
    [property: JsonPropertyName("metadataId")] long MetadataId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("trainState")] string TrainState,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("failureJunction")] string? FailureJunction,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("executor")] string? Executor,
    [property: JsonPropertyName("output")] string? Output,
    [property: JsonPropertyName("hostName")] string? HostName = null,
    [property: JsonPropertyName("hostEnvironment")] string? HostEnvironment = null,
    [property: JsonPropertyName("changeDomain")] string? ChangeDomain = null
)
{
    /// <summary>
    /// <see cref="EventType"/> value marking a coalesced data-change signal rather than a train
    /// lifecycle transition. Handlers that only care about train events ignore it.
    /// </summary>
    public const string DataChangedEventType = "DataChanged";

    /// <summary><see cref="EventType"/> of a junction that started.</summary>
    public const string JunctionStartedEventType = "JunctionStarted";

    /// <summary><see cref="EventType"/> of a junction that returned a result.</summary>
    public const string JunctionCompletedEventType = "JunctionCompleted";

    /// <summary><see cref="EventType"/> of a junction that failed.</summary>
    public const string JunctionFailedEventType = "JunctionFailed";

    /// <summary><see cref="EventType"/> of a junction stopped by a cancellation the run was asked for.</summary>
    public const string JunctionCancelledEventType = "JunctionCancelled";

    /// <summary><see cref="EventType"/> of a question a routing step asked and the run acts on the answer to.</summary>
    public const string DecidedEventType = "Decided";

    /// <summary>
    /// <see cref="EventType"/> of a decider's answer the run would not act on (missing, or not
    /// fitting the question), on which the routing step fails.
    /// </summary>
    public const string DecisionRefusedEventType = "DecisionRefused";

    /// <summary><see cref="EventType"/> of the track a routing step sent the run down.</summary>
    public const string RoutedEventType = "Routed";

    /// <summary>
    /// Whether <paramref name="eventType"/> is one of the junction event types, which carry a
    /// <see cref="Junction"/> payload and are delivered to <see cref="IJunctionEventHandler"/>s
    /// rather than <see cref="ITrainEventHandler"/>s.
    /// </summary>
    /// <param name="eventType">A message's <see cref="EventType"/>.</param>
    public static bool IsJunctionEvent(string? eventType) =>
        eventType
            is JunctionStartedEventType
                or JunctionCompletedEventType
                or JunctionFailedEventType
                or JunctionCancelledEventType
                or DecidedEventType
                or DecisionRefusedEventType
                or RoutedEventType;

    /// <summary>
    /// The step of the run a junction event is about, or <c>null</c> for any other message. Set
    /// exactly when <see cref="IsJunctionEvent"/> is true of <see cref="EventType"/>. A junction
    /// event leaves <see cref="Output"/>, <see cref="FailureReason"/>, <see cref="FailureJunction"/>
    /// and <see cref="FailureException"/> null: it carries only what <see cref="JunctionEventPayload"/>
    /// holds.
    /// </summary>
    [JsonPropertyName("junction")]
    public JunctionEventPayload? Junction { get; init; }

    /// <summary>
    /// Identifies the host that published the message, one value per running host. A receiver
    /// drops a message carrying its own host's id, because that host's in-process hooks already
    /// delivered it, and delivers every other one. Replicas of one app share an
    /// <see cref="Executor"/> but not an instance id, so each sees the others' events. A message
    /// from a publisher that predates the field has none, and is treated as coming from another
    /// host. <see cref="Executor"/> stays the name shown for display.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; init; }

    /// <summary>
    /// The type name of the exception a failed run recorded (<c>Metadata.FailureException</c>),
    /// such as <c>TrainException</c>, or <c>null</c> for any other event. A receiver uses it to
    /// decide, as the publishing host would, whether <see cref="FailureReason"/> is a message meant
    /// for the caller. A message from a publisher that predates the field has none.
    /// </summary>
    [JsonPropertyName("failureException")]
    public string? FailureException { get; init; }
}
