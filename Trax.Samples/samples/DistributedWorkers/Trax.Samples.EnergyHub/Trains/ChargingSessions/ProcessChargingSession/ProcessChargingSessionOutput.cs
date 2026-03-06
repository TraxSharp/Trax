namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;

public record ProcessChargingSessionOutput
{
    public required string StationId { get; init; }
    public int SessionsProcessed { get; init; }
    public decimal RevenueGenerated { get; init; }
}
