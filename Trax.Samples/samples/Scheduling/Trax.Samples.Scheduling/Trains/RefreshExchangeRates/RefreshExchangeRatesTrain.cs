using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduling.Trains.RefreshExchangeRates.Junctions;

namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates;

/// <summary>
/// Runs on an interval. Its success is what fires the <c>reprice-catalog</c> dependent, and when
/// a reading is a spike, <see cref="FlagSpikeJunction"/> activates the dormant
/// <c>alert-rate-spike</c> manifest with that reading as its input.
/// </summary>
public class RefreshExchangeRatesTrain
    : ServiceTrain<RefreshExchangeRatesInput, RefreshExchangeRatesOutput>,
        IRefreshExchangeRatesTrain
{
    protected override Task<Either<Exception, RefreshExchangeRatesOutput>> Junctions() =>
        Chain<FetchRatesJunction>().Chain<FlagSpikeJunction>().Resolve();
}
