using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.LowStockCheck;

public record LowStockCheckInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
