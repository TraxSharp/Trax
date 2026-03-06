using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;

public record GenerateSustainabilityReportInput : IManifestProperties
{
    public required string ReportPeriod { get; init; }
}
