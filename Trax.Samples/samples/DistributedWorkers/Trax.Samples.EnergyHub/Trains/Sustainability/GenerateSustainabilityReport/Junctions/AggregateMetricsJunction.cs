using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport.Junctions;

public class AggregateMetricsJunction(ILogger<AggregateMetricsJunction> logger)
    : Junction<GenerateSustainabilityReportInput, GenerateSustainabilityReportInput>
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
