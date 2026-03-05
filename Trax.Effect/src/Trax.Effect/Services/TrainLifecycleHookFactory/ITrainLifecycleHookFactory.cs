using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Services.TrainLifecycleHookFactory;

/// <summary>
/// Factory for creating <see cref="ITrainLifecycleHook"/> instances.
/// Registered via <c>AddLifecycleHook&lt;TFactory&gt;()</c> on the effect configuration builder.
/// </summary>
public interface ITrainLifecycleHookFactory
{
    ITrainLifecycleHook Create();
}
