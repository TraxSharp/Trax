using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid.Junctions;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid;

/// <summary>
/// Optimizes energy distribution across the microgrid by balancing supply
/// from solar, battery, and grid sources against demand from the service plaza,
/// data centers, and EV charging stations.
/// Scheduled every 15 minutes.
/// </summary>
[TraxMutation(
    GraphQLOperation.Queue,
    Description = "Optimizes energy distribution across the microgrid"
)]
public class OptimizeMicrogridTrain
    : ServiceTrain<OptimizeMicrogridInput, Unit>,
        IOptimizeMicrogridTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        OptimizeMicrogridInput input
    ) =>
        Activate(input)
            .Chain<GatherEnergyMetricsJunction>()
            .Chain<ApplyDistributionJunction>()
            .Resolve();
}
