using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Trains.Discover.Junctions;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>
/// Lists the partitions the indexes hold, one per source and month. Running it again lists the
/// same partitions.
/// </summary>
public class DiscoverPartitionsTrain
    : ServiceTrain<DiscoverPartitionsInput, DiscoveredPartitions>,
        IDiscoverPartitionsTrain
{
    protected override Task<Either<Exception, DiscoveredPartitions>> Junctions() =>
        Chain<ListPartitions>().Resolve();
}
