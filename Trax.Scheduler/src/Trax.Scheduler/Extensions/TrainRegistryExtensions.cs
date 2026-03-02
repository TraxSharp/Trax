using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for <see cref="ITrainRegistry"/> used by the scheduler.
/// </summary>
public static class TrainRegistryExtensions
{
    /// <summary>
    /// Validates that a train is registered for the specified input type.
    /// </summary>
    /// <typeparam name="TInput">The input type to validate</typeparam>
    /// <param name="registry">The train registry to check</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no train is registered for the input type.
    /// </exception>
    public static void ValidateTrainRegistration<TInput>(this ITrainRegistry registry) =>
        registry.ValidateTrainRegistration(typeof(TInput));

    internal static void ValidateTrainRegistration(this ITrainRegistry registry, Type inputType)
    {
        if (!registry.InputTypeToTrain.ContainsKey(inputType))
        {
            throw new InvalidOperationException(
                $"Train for input type '{inputType.Name}' is not registered in the TrainRegistry. "
                    + $"Ensure the train assembly is included in AddEffectTrainBus()."
            );
        }
    }
}
