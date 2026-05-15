using System.Text.Json.Serialization;

namespace Trax.Effect.Broadcaster.SignalR.Models;

/// <summary>
/// Default narrow payload sent to SignalR clients for train lifecycle events.
/// A subset of <see cref="Services.TrainEventBroadcaster.TrainLifecycleEventMessage"/>
/// containing only fields a UI typically renders. Replace via
/// <c>SignalRSinkOptions.WithProjection&lt;T&gt;()</c> when a different shape is needed.
/// </summary>
public record TraxClientEvent(
    [property: JsonPropertyName("metadataId")] long MetadataId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("failureReason")] string? FailureReason
);
