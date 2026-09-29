using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Services.TrainLifecycleHookFactory;

/// <summary>
/// Factory for creating <see cref="ITrainLifecycleHook"/> instances.
/// Registered via <c>AddLifecycleHook&lt;TFactory&gt;()</c> on the effect configuration builder.
/// </summary>
public interface ITrainLifecycleHookFactory
{
    /// <summary>
    /// Creates a hook with no run scope. The runner uses <see cref="Create(IServiceProvider)"/> instead, whose
    /// default calls this; implement this one when the hook needs no scoped services.
    /// </summary>
    ITrainLifecycleHook Create();

    /// <summary>
    /// Creates the hook for one run, from the services of that run's scope.
    /// </summary>
    /// <remarks>
    /// The factory is a singleton, so a hook built from the factory's own provider gets the root
    /// container's services: a scoped dependency such as <c>IDataContext</c> is then either refused
    /// (when the container validates scopes) or one instance shared by every run in the process.
    /// The runner calls this overload with the run's scope. The default calls
    /// <see cref="Create()"/>, so a factory written against the earlier interface keeps working;
    /// override it when the hook takes scoped services.
    /// </remarks>
    /// <param name="serviceProvider">The service provider of the run's scope.</param>
    ITrainLifecycleHook Create(IServiceProvider serviceProvider) => Create();
}
