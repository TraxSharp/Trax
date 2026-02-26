using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataProcessing;

public interface IDataProcessingPipelineWorkflow : IServiceTrain<DataProcessingPipelineInput, Unit>;
