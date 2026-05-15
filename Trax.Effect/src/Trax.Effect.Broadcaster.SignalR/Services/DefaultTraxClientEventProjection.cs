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
            FailureReason: message.FailureReason
        );
}
