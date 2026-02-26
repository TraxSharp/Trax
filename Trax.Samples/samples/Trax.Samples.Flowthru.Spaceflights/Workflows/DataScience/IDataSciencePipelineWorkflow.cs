using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience;

public interface IDataSciencePipelineWorkflow : IServiceTrain<DataSciencePipelineInput, Unit>;
