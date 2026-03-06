using System.Text.Json.Serialization;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Serializable message representing a train lifecycle event for cross-process broadcasting.
/// This is the payload that flows through the message bus between worker and hub processes.
/// </summary>
public record TrainLifecycleEventMessage(
    [property: JsonPropertyName("metadataId")] long MetadataId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("trainState")] string TrainState,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("failureStep")] string? FailureStep,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("executor")] string? Executor
);
