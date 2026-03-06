using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataScience;

public interface IDataScienceTrain : IServiceTrain<DataSciencePipelineInput, Unit>;
