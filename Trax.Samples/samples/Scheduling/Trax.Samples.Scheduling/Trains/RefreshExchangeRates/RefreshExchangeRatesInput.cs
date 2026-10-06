using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates;

/// <summary>
/// A scheduled train's input implements <see cref="IManifestProperties"/>: the manifest stores it
/// as JSON and hands it to every run.
/// </summary>
public record RefreshExchangeRatesInput : IManifestProperties
{
    public string BaseCurrency { get; init; } = "USD";

    /// <summary>A move larger than this, in percent, activates the dormant spike alert.</summary>
    public decimal SpikeThresholdPercent { get; init; } = 5m;
}
