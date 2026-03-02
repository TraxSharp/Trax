using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataProcessing;

public interface IDataProcessingTrain : IServiceTrain<DataProcessingPipelineInput, Unit>;
