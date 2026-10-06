using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.AlertRateSpike;

/// <summary>
/// The manifest is seeded with the defaults; each activation supplies the real reading.
/// </summary>
public record AlertRateSpikeInput : IManifestProperties
{
    public string BaseCurrency { get; init; } = "USD";
    public decimal ChangePercent { get; init; }
}
