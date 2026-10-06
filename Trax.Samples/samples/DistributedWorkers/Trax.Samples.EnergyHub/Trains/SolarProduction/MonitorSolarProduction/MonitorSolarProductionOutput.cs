namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

public record MonitorSolarProductionOutput
{
    public required string ArrayId { get; init; }
    public double TotalKwh { get; init; }
    public double Efficiency { get; init; }
}
