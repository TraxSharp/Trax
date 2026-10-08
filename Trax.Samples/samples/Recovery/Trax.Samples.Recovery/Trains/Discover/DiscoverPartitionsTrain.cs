using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Discover.Junctions;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>
/// Lists the partitions the indexes hold, one per source and month, and starts one system-owned
/// <c>source-partition</c> machine instance for each, which ingests it. Running it again lists the
/// same partitions, finds the same instances and queues nothing new.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Lists the index partitions and starts one ingest machine for each"
)]
public class DiscoverPartitionsTrain
    : ServiceTrain<DiscoverPartitionsInput, DiscoveredPartitions>,
        IDiscoverPartitionsTrain
{
    protected override Task<Either<Exception, DiscoveredPartitions>> Junctions() =>
        Chain<ListPartitions>().Chain<StartPartitionMachines>().Resolve();
}
