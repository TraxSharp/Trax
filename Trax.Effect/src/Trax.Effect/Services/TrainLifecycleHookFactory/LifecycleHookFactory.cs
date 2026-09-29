using System.ComponentModel;
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
[EditorBrowsable(EditorBrowsableState.Never)]
public class LifecycleHookFactory<THook>(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
    where THook : class, ITrainLifecycleHook
{
    /// <summary>
    /// Creates a <typeparamref name="THook"/> from the root container the factory was resolved from, so scoped
    /// dependencies are not the run's.
    /// </summary>
    public ITrainLifecycleHook Create() =>
        ActivatorUtilities.CreateInstance<THook>(serviceProvider);

    /// <summary>
    /// Creates a <typeparamref name="THook"/> with <see cref="ActivatorUtilities"/> from the run's scope, so its
    /// constructor receives that run's scoped services. A new hook is built for every run.
    /// </summary>
    /// <param name="scopeProvider">The service provider of the run's scope.</param>
    public ITrainLifecycleHook Create(IServiceProvider scopeProvider) =>
        ActivatorUtilities.CreateInstance<THook>(scopeProvider);
}
