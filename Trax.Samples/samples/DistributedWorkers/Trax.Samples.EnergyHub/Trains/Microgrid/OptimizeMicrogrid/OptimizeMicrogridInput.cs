using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid;

public record OptimizeMicrogridInput : IManifestProperties
{
    public required string GridZone { get; init; }
}
