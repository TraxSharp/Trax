using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport.Steps;

public class PublishReportStep(ILogger<PublishReportStep> logger)
    : Step<GenerateSustainabilityReportInput, GenerateSustainabilityReportOutput>
{
    public override async Task<GenerateSustainabilityReportOutput> Run(
        GenerateSustainabilityReportInput input
    )
    {
        logger.LogInformation("[{Period}] Publishing sustainability report", input.ReportPeriod);

        await Task.Delay(200);

        logger.LogInformation(
            "[{Period}] Report published — 82% renewable, 12.4 tons CO₂ offset, $156.80 revenue",
            input.ReportPeriod
        );

        return new GenerateSustainabilityReportOutput
        {
            ReportPeriod = input.ReportPeriod,
            CarbonOffsetTons = 12.4,
            RenewablePercent = 82.0,
            TotalKwhGenerated = 3420.5,
            TotalRevenue = 156.80m,
        };
    }
}
