namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Handles train lifecycle events received from the message bus.
/// Register implementations to react to cross-process lifecycle events
/// (e.g., forwarding to GraphQL subscriptions).
/// </summary>
public interface ITrainEventHandler
{
    Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
