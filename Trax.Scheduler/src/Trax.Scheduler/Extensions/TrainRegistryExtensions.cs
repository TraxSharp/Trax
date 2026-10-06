using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for <see cref="ITrainRegistry"/> used by the scheduler.
/// </summary>
internal static class TrainRegistryExtensions
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
                $"No train implements IServiceTrain<{inputType.Name}, TOut>. Add the train's "
                    + "assembly to AddMediator(m => m.ScanAssemblies(typeof(MyTrain).Assembly))."
            );
        }
    }

    /// <summary>
    /// Validates that <paramref name="trainType"/> itself is a registered train, not only that some
    /// train takes <paramref name="inputType"/>. A scheduled run runs the train it names, so a
    /// train that is not registered would otherwise be accepted here and fail at every dispatch.
    /// </summary>
    /// <param name="registry">The train registry, for the input-type check.</param>
    /// <param name="discovery">
    /// The registered trains. Null only when the scheduler was constructed without one, in which
    /// case only the input type is checked.
    /// </param>
    /// <param name="trainType">The train being scheduled: its interface or its class.</param>
    /// <param name="inputType">The train's input type.</param>
    internal static void ValidateTrainRegistration(
        this ITrainRegistry registry,
        ITrainDiscoveryService? discovery,
        Type trainType,
        Type inputType
    )
    {
        registry.ValidateTrainRegistration(inputType);

        if (discovery is null)
            return;

        foreach (var registration in discovery.DiscoverTrains())
            if (
                registration.ServiceType == trainType
                || registration.ImplementationType == trainType
            )
                return;

        throw new InvalidOperationException(
            $"Train '{trainType.FullName}' is not registered, although another train takes "
                + $"{inputType.Name}. A scheduled run runs the train it names. Add the train's "
                + $"assembly to AddMediator(m => m.ScanAssemblies(typeof({trainType.Name}).Assembly))."
        );
    }
}
