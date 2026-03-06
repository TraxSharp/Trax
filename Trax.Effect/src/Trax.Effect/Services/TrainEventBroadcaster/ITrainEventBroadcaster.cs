namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Publishes train lifecycle events to an external message bus for cross-process delivery.
/// Implementations handle the transport details (e.g., RabbitMQ, Redis, etc.).
/// </summary>
public interface ITrainEventBroadcaster
{
    Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
