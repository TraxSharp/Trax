namespace Trax.Mediator.Services.TrainDiscovery;

/// <summary>
/// Discovers all IServiceTrain registrations available in the DI container.
/// </summary>
public interface ITrainDiscoveryService
{
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
