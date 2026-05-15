using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// Returns the singleton <see cref="SignalRTrainEventDispatcher"/> from each <c>Create()</c>
/// call so that lifecycle hook runners reuse the one dispatcher instance shared with the
/// remote-event handler path.
/// </summary>
internal sealed class SignalRTrainEventDispatcherFactory : ITrainLifecycleHookFactory
{
    private readonly SignalRTrainEventDispatcher _dispatcher;

    public SignalRTrainEventDispatcherFactory(SignalRTrainEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ITrainLifecycleHook Create() => _dispatcher;
}
