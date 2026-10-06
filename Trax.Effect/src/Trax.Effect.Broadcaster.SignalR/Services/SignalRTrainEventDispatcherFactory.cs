using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// Hands each run a hook that forwards to the one singleton <see cref="SignalRTrainEventDispatcher"/>,
/// which is shared with the remote-event handler path.
/// </summary>
/// <remarks>
/// The lifecycle hook runner disposes every hook it was given that is <see cref="IDisposable"/> when
/// its run ends. The dispatcher is disposable and outlives every run, so it is never handed out
/// itself: the forwarder is not disposable, and the host's container alone disposes the dispatcher.
/// </remarks>
internal sealed class SignalRTrainEventDispatcherFactory : ITrainLifecycleHookFactory
{
    private readonly ITrainLifecycleHook _hook;

    public SignalRTrainEventDispatcherFactory(SignalRTrainEventDispatcher dispatcher)
    {
        _hook = new SharedDispatcherHook(dispatcher);
    }

    public ITrainLifecycleHook Create() => _hook;

    /// <summary>
    /// Forwards every lifecycle event to the shared dispatcher and owns nothing, so a run's
    /// disposal of its hooks leaves the dispatcher running.
    /// </summary>
    private sealed class SharedDispatcherHook(SignalRTrainEventDispatcher dispatcher)
        : ITrainLifecycleHook
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) =>
            dispatcher.OnStarted(metadata, ct);

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            dispatcher.OnCompleted(metadata, ct);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            dispatcher.OnFailed(metadata, exception, ct);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            dispatcher.OnCancelled(metadata, ct);

        public Task OnStateChanged(Metadata metadata, CancellationToken ct) =>
            dispatcher.OnStateChanged(metadata, ct);
    }
}
