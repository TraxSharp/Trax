namespace Trax.Samples.JobHunt.Subscriptions;

public record JobHuntSubscriptionEvent(
    string EventType,
    string? Payload,
    DateTime Timestamp,
    string? TrainExternalId,
    string? UserId,
    Guid? JobId
);
