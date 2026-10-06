namespace Trax.Samples.ChatService.Subscriptions;

public record ChatSubscriptionEvent(
    Guid ChatRoomId,
    string EventType,
    string Payload,
    DateTime Timestamp,
    string TrainExternalId
);
