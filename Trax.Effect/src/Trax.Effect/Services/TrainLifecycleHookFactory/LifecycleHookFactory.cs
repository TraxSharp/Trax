using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Services.TrainLifecycleHookFactory;

/// <summary>
/// Generic factory that creates lifecycle hook instances via DI.
/// Used internally by the <c>AddLifecycleHook&lt;THook&gt;()</c> overload
/// so that users don't need to write their own factory classes.
/// </summary>
/// <remarks>
/// The runner builds each run's hook through <see cref="Create(IServiceProvider)"/> with the run's
/// scope, so the hook's constructor receives that scope's services. <see cref="Create()"/> builds
/// from the provider the factory was resolved from, which is the root container.
/// </remarks>
public class LifecycleHookFactory<THook>(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
    where THook : class, ITrainLifecycleHook
{
    public ITrainLifecycleHook Create() =>
        ActivatorUtilities.CreateInstance<THook>(serviceProvider);

    public ITrainLifecycleHook Create(IServiceProvider scopeProvider) =>
        ActivatorUtilities.CreateInstance<THook>(scopeProvider);
}
