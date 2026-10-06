namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport;

public record GenerateModerationReportOutput
{
    public int TotalReviewed { get; init; }
    public int TotalFlagged { get; init; }
    public required string[] TopViolationTypes { get; init; }
    public double FalsePositiveRate { get; init; }
}
