using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport.Steps;

public class AggregateMetricsStep(ILogger<AggregateMetricsStep> logger)
    : Step<GenerateSustainabilityReportInput, GenerateSustainabilityReportInput>
{
    public override async Task<GenerateSustainabilityReportInput> Run(
        GenerateSustainabilityReportInput input
    )
    {
        logger.LogInformation(
            "[{Period}] Aggregating energy hub metrics for sustainability report",
            input.ReportPeriod
        );

        await Task.Delay(400);

        logger.LogInformation(
            "[{Period}] Metrics aggregated — solar, battery, charging, and grid trading data compiled",
            input.ReportPeriod
        );

        return input;
    }
}
