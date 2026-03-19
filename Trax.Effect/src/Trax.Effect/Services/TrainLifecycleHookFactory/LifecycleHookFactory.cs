using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Services.TrainLifecycleHookFactory;

/// <summary>
/// Generic factory that creates lifecycle hook instances via DI.
/// Used internally by the <c>AddLifecycleHook&lt;THook&gt;()</c> overload
/// so that users don't need to write their own factory classes.
/// </summary>
public class LifecycleHookFactory<THook>(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
    where THook : class, ITrainLifecycleHook
{
    public ITrainLifecycleHook Create() =>
        ActivatorUtilities.CreateInstance<THook>(serviceProvider);
}
