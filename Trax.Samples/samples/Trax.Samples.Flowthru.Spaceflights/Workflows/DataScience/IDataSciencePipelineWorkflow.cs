using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience;

public interface IDataSciencePipelineWorkflow : IServiceTrain<DataSciencePipelineInput, Unit>;
