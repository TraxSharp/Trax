using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport.Junctions;

namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport;

/// <summary>
/// Generates a summary report of moderation activity for the specified period.
/// Scheduled daily at midnight. Can also be run on-demand via GraphQL.
/// </summary>
[TraxConcurrencyLimit(5)]
[TraxMutation(Namespace = "reports", Description = "Generates a moderation activity report")]
[TraxBroadcast]
public class GenerateModerationReportTrain
    : ServiceTrain<GenerateModerationReportInput, GenerateModerationReportOutput>,
        IGenerateModerationReportTrain
{
    protected override async Task<Either<Exception, GenerateModerationReportOutput>> RunInternal(
        GenerateModerationReportInput input
    ) => Activate(input).Chain<AggregateMetricsJunction>().Chain<FormatReportJunction>().Resolve();
}
