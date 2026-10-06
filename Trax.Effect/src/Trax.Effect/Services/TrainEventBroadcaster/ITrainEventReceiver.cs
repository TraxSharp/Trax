namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Receives train lifecycle events from an external message bus.
/// Implementations handle the transport details (e.g., RabbitMQ, Redis, etc.).
/// </summary>
public interface ITrainEventReceiver : IAsyncDisposable
{
    /// <summary>
    /// Connects to the transport and starts delivering each received message to <paramref name="handler"/>.
    /// Returns once consumption has started; delivery continues in the background until
    /// <see cref="StopAsync"/>. Throwing makes the receiver service call <see cref="StopAsync"/> and retry with
    /// exponential backoff from 5 seconds up to 2 minutes.
    /// </summary>
    /// <param name="handler">Called for every received message.</param>
    /// <param name="ct">The host's stopping token.</param>
    Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    );

    /// <summary>
    /// Stops consuming and releases the connection. Called on host shutdown and before each reconnect attempt, so
    /// it must tolerate being called when <see cref="StartAsync"/> never succeeded.
    /// </summary>
    /// <param name="ct">Bounds the shutdown.</param>
    Task StopAsync(CancellationToken ct);
}
