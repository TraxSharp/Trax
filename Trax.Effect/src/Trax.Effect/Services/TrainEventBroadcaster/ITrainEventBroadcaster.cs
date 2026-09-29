namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Publishes train lifecycle events to an external message bus for cross-process delivery.
/// Implementations handle the transport details (e.g., RabbitMQ, Redis, etc.).
/// </summary>
public interface ITrainEventBroadcaster
{
    /// <summary>
    /// Publishes <paramref name="message"/> to every process subscribed through an <see cref="ITrainEventReceiver"/>.
    /// Called by the broadcast lifecycle hook and change sink; an exception is logged by the caller and not retried.
    /// </summary>
    /// <param name="message">The lifecycle event or data-change signal.</param>
    /// <param name="ct">Cancels the publish.</param>
    Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
