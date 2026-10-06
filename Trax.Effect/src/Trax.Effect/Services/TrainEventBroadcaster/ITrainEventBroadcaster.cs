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
    /// On a host that called <c>AddJunctionEvents()</c> it is also handed every junction event, each a
    /// message whose <see cref="TrainLifecycleEventMessage.Junction"/> is set
    /// (<see cref="TrainLifecycleEventMessage.IsJunctionEvent"/>), on the run's path; a transport
    /// should queue rather than wait, and may route them apart from train events.
    /// </summary>
    /// <param name="message">The lifecycle event or data-change signal.</param>
    /// <param name="ct">Cancels the publish.</param>
    Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
