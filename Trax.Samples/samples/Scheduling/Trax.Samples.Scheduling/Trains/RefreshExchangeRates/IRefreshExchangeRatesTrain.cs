using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates;

public interface IRefreshExchangeRatesTrain
    : IServiceTrain<RefreshExchangeRatesInput, RefreshExchangeRatesOutput>;
