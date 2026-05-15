using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// No-op transport used when <see cref="Extensions.SignalRBroadcasterExtensions.UseSignalRHub"/>
/// is configured without a cross-process transport (e.g. RabbitMQ). The broadcaster pipeline
/// always registers <c>TrainEventReceiverService</c> and <c>BroadcastLifecycleHook</c>, which
/// require an <see cref="ITrainEventBroadcaster"/> and <see cref="ITrainEventReceiver"/>; these
/// no-ops let the SignalR sink run standalone in a single-process topology. Registered via
/// <c>TryAdd*</c> so a real transport (e.g. <c>UseRabbitMq</c>) takes precedence.
/// </summary>
internal sealed class NullTrainEventBroadcaster : ITrainEventBroadcaster
{
    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct) =>
        Task.CompletedTask;
}

internal sealed class NullTrainEventReceiver : ITrainEventReceiver
{
    public Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    ) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
