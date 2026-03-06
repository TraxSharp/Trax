using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Factory that creates <see cref="BroadcastLifecycleHook"/> instances via DI.
/// </summary>
public class BroadcastLifecycleHookFactory(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() =>
        serviceProvider.GetRequiredService<BroadcastLifecycleHook>();
}
