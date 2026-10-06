using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.AlertRateSpike.Junctions;

namespace Trax.Samples.Scheduling.Trains.AlertRateSpike;

/// <summary>
/// A dormant dependent of <c>refresh-exchange-rates</c>: never run on a timer or by its parent's
/// success, only when the parent activates it.
/// </summary>
public class AlertRateSpikeTrain : ServiceTrain<AlertRateSpikeInput, Unit>, IAlertRateSpikeTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<NotifyTreasuryJunction>().Resolve();
}
