namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates;

public record RefreshExchangeRatesOutput(
    string BaseCurrency,
    decimal ChangePercent,
    bool SpikeDetected
);
