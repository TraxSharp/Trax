using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Services;

internal static class DefaultTraxClientEventProjection
{
    public static object Project(TrainLifecycleEventMessage message) =>
        new TraxClientEvent(
            MetadataId: message.MetadataId,
            ExternalId: message.ExternalId,
            TrainName: message.TrainName,
            EventType: message.EventType,
            Timestamp: message.Timestamp,
            // A failure reason can hold whatever the failing code put in its exception message, and
            // every connected client receives every train's events. A host that wants it on the wire
            // chooses that with WithProjection.
            FailureReason: null
        );
}
