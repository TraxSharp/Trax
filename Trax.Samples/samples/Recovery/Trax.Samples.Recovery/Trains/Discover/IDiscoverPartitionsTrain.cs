using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Discover;

public interface IDiscoverPartitionsTrain
    : IServiceTrain<DiscoverPartitionsInput, DiscoveredPartitions>;
