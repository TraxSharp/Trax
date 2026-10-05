using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.Scheduling.Services;

namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates.Junctions;

public class FetchRatesJunction(ExchangeRateFeed feed, ILogger<FetchRatesJunction> logger)
    : Junction<RefreshExchangeRatesInput, RateReading>
{
    public override Task<RateReading> Run(RefreshExchangeRatesInput input)
    {
        var change = feed.NextChangePercent();
        logger.LogInformation("{Currency} moved {Change}%", input.BaseCurrency, change);
        return Task.FromResult(new RateReading(input.BaseCurrency, change));
    }
}
