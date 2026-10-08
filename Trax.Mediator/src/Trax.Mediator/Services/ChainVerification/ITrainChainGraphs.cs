using Trax.Core.Monad;

namespace Trax.Mediator.Services.ChainVerification;

/// <summary>
/// The declared chain of each registered train, as a <see cref="ChainGraph"/>, for anything that
/// draws a train or matches a run's steps to it.
/// </summary>
/// <remarks>
/// Each train's chain is read once, the first time it is asked for, and kept: a chain is a
/// declaration, so it cannot change while the host runs. Only registered trains are read, looked up
/// by name, so a caller cannot make the host load or build a type it chose.
/// </remarks>
public interface ITrainChainGraphs
{
    /// <summary>
    /// The graph of the registered train named <paramref name="train"/>, its canonical name
    /// (<see cref="TrainDiscovery.TrainRegistration.ServiceType"/>'s full name, as run metadata
    /// records it), or null when no registered train has that name or its chain cannot be read here.
    /// </summary>
    /// <remarks>
    /// A chain cannot be read here when the train itself cannot be built outside a request, for a
    /// dependency only a request supplies; the startup check skips such a train with a warning
    /// for the same reason.
    /// </remarks>
    ChainGraph? Find(string train);
}
