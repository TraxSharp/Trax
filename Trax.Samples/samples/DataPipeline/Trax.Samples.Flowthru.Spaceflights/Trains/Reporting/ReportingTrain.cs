using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Flowthru.Spaceflights.Trains.Reporting.Steps;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.Reporting;

/// <summary>
/// Wraps the flowthru Reporting pipeline as a Trax.Core ServiceTrain.
/// Generates passenger capacity reports, charts, and PNG exports.
/// </summary>
public class ReportingTrain : ServiceTrain<ReportingPipelineInput, Unit>, IReportingTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        ReportingPipelineInput input
    ) => Activate(input).Chain<ExecuteReportingStep>().Resolve();
}
