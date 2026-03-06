using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

public record MonitorSolarProductionInput : IManifestProperties
{
    public required string ArrayId { get; init; }
    public required string Region { get; init; }
}
