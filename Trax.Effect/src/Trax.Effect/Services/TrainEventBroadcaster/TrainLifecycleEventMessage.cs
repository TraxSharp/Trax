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
}
