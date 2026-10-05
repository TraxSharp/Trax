using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.RepriceCatalog.Junctions;

namespace Trax.Samples.Scheduling.Trains.RepriceCatalog;

/// <summary>
/// A dependent of <c>refresh-exchange-rates</c>. It has no schedule of its own: the scheduler
/// queues it once its parent has succeeded since this train's latest run started.
/// </summary>
public class RepriceCatalogTrain : ServiceTrain<RepriceCatalogInput, Unit>, IRepriceCatalogTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<ApplyRatesJunction>().Resolve();
}
