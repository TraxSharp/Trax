namespace Trax.Mediator.Services.TrainDiscovery;

/// <summary>
/// Discovers all IServiceTrain registrations available in the DI container.
/// </summary>
public interface ITrainDiscoveryService
{
    /// <summary>
    /// Scans the DI container for all <see cref="Trax.Effect.Services.ServiceTrain.IServiceTrain{TIn,TOut}"/>
    /// registrations and returns their metadata (input/output types, lifetime, authorization requirements, GraphQL attributes).
    /// Results are cached after the first call.
    /// </summary>
    /// <returns>A deduplicated list of train registrations.</returns>
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
