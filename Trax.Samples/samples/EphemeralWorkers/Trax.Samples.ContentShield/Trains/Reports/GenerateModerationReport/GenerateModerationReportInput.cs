namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport;

public record GenerateModerationReportInput
{
    public required string ReportPeriod { get; init; }
}
