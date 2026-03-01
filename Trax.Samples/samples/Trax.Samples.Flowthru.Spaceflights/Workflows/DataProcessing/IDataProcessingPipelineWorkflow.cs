using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataProcessing;

public interface IDataProcessingPipelineWorkflow : IServiceTrain<DataProcessingPipelineInput, Unit>;
