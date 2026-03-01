using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting;

public interface IReportingPipelineWorkflow : IServiceTrain<ReportingPipelineInput, Unit>;
