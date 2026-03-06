namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Receives train lifecycle events from an external message bus.
/// Implementations handle the transport details (e.g., RabbitMQ, Redis, etc.).
/// </summary>
public interface ITrainEventReceiver : IAsyncDisposable
{
    Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    );

    Task StopAsync(CancellationToken ct);
}
