using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport.Junctions;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;

/// <summary>
/// Generates a daily sustainability report aggregating all energy hub metrics:
/// carbon offset, renewable percentage, total generation, and revenue.
/// Scheduled daily at midnight via Cron.
/// </summary>
[TraxMutation(
    Namespace = "sustainability",
    Description = "Generates a sustainability report for the energy hub"
)]
[TraxBroadcast]
public class GenerateSustainabilityReportTrain
    : ServiceTrain<GenerateSustainabilityReportInput, GenerateSustainabilityReportOutput>,
        IGenerateSustainabilityReportTrain
{
    protected override GenerateSustainabilityReportOutput Junctions() =>
        Chain<AggregateMetricsJunction>().Chain<PublishReportJunction>();
}
