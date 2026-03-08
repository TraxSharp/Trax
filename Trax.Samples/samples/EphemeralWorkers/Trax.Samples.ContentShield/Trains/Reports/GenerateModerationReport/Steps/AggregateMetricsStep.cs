using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport.Steps;

/// <summary>
/// Aggregates moderation metrics from the database for the requested period.
/// </summary>
public class AggregateMetricsStep(ILogger<AggregateMetricsStep> logger)
    : Step<GenerateModerationReportInput, GenerateModerationReportInput>
{
    public override async Task<GenerateModerationReportInput> Run(
        GenerateModerationReportInput input
    )
    {
        logger.LogInformation(
            "Aggregating moderation metrics for period: {ReportPeriod}",
            input.ReportPeriod
        );

        await Task.Delay(250);

        logger.LogInformation("Metrics aggregated: 1,247 items reviewed, 83 flagged");

        return input;
    }
}
