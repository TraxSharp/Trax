using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.Restock;

public record RestockInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
