namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Handles train lifecycle events received from the message bus.
/// Register implementations to react to cross-process lifecycle events
/// (e.g., forwarding to GraphQL subscriptions).
/// </summary>
public interface ITrainEventHandler
{
    /// <summary>
    /// Handles one event that arrived from another host; events this host published are filtered out before
    /// handlers run. Handlers are resolved from a new scope per message. An exception is logged and does not stop
    /// the other handlers or the receiver.
    /// </summary>
    /// <param name="message">The received event.</param>
    /// <param name="ct">The receiver's delivery token.</param>
    Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
