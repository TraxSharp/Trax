using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport.Junctions;

/// <summary>
/// Formats the aggregated metrics into a structured report output.
/// </summary>
public class FormatReportJunction(ILogger<FormatReportJunction> logger)
    : Junction<GenerateModerationReportInput, GenerateModerationReportOutput>
{
    public override async Task<GenerateModerationReportOutput> Run(
        GenerateModerationReportInput input
    )
    {
        logger.LogInformation("Formatting {ReportPeriod} moderation report", input.ReportPeriod);

        await Task.Delay(100);

        return new GenerateModerationReportOutput
        {
            TotalReviewed = 1247,
            TotalFlagged = 83,
            TopViolationTypes = ["violence", "spam", "hate-speech"],
            FalsePositiveRate = 0.042,
        };
    }
}
