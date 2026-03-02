namespace Trax.Dashboard.Services.TrainDiscovery;

/// <summary>
/// Discovers all IEffectTrain registrations available in the DI container.
/// </summary>
public interface ITrainDiscoveryService
{
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
