using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting;

public interface IReportingPipelineWorkflow : IServiceTrain<ReportingPipelineInput, Unit>;
