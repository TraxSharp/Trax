using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport.Junctions;

public class PublishReportJunction(ILogger<PublishReportJunction> logger)
    : Junction<GenerateSustainabilityReportInput, GenerateSustainabilityReportOutput>
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
