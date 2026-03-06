namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;

public record GenerateSustainabilityReportOutput
{
    public required string ReportPeriod { get; init; }
    public double CarbonOffsetTons { get; init; }
    public double RenewablePercent { get; init; }
    public double TotalKwhGenerated { get; init; }
    public decimal TotalRevenue { get; init; }
}
