using System.Text.Json.Serialization;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Serializable message representing a train lifecycle event for cross-process broadcasting.
/// This is the payload that flows through the message bus between worker and hub processes.
/// The same transport also carries coalesced data-change signals: those set
/// <see cref="EventType"/> to <see cref="DataChangedEventType"/> and put the changed domain in
/// <see cref="ChangeDomain"/>, leaving the train-specific fields empty.
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
