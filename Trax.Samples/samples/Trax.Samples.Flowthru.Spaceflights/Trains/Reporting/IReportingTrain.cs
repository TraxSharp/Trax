using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.Reporting;

public interface IReportingTrain : IServiceTrain<ReportingPipelineInput, Unit>;
