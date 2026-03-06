using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;

public record ProcessChargingSessionInput : IManifestProperties
{
    public required string StationId { get; init; }
    public required string SessionType { get; init; }
}
