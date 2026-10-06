using System.Text.Json.Serialization;

namespace Trax.Effect.Broadcaster.SignalR.Models;

/// <summary>
/// Default narrow payload sent to SignalR clients for train lifecycle events.
/// A subset of <see cref="Trax.Effect.Services.TrainEventBroadcaster.TrainLifecycleEventMessage"/>
/// containing only fields a UI typically renders. Replace via
/// <c>SignalRSinkOptions.WithProjection&lt;T&gt;()</c> when a different shape is needed.
/// The default projection leaves <see cref="FailureReason"/> null, and a null is left off the
/// wire: every connected client receives every train's events, and a failure reason carries
/// whatever the failing code put in its exception message. A projection that should send it
/// sets it.
/// </summary>
public record TraxClientEvent(
    [property: JsonPropertyName("metadataId")] long MetadataId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("failureReason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? FailureReason
);
