using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting.Steps;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting;

/// <summary>
/// Wraps the flowthru Reporting pipeline as a Trax.Core ServiceTrain.
/// Generates passenger capacity reports, charts, and PNG exports.
/// </summary>
public class ReportingPipelineWorkflow
    : ServiceTrain<ReportingPipelineInput, Unit>,
        IReportingPipelineWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        ReportingPipelineInput input
    ) => Activate(input).Chain<ExecuteReportingStep>().Resolve();
}
