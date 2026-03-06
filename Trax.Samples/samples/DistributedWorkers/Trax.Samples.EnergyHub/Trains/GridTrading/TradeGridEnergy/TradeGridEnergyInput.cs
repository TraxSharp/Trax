using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;

public record TradeGridEnergyInput : IManifestProperties
{
    public decimal RatePerKwh { get; init; } = 0.14m;
    public int MaxSellPercent { get; init; } = 80;
}
