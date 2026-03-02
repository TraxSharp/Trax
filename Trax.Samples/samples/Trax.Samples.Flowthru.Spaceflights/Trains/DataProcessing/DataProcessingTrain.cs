using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Flowthru.Spaceflights.Trains.DataProcessing.Steps;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataProcessing;

/// <summary>
/// Wraps the flowthru DataProcessing pipeline as a Trax.Core ServiceTrain.
/// Preprocesses raw company, shuttle, and review data into a model input table.
/// </summary>
public class DataProcessingTrain
    : ServiceTrain<DataProcessingPipelineInput, Unit>,
        IDataProcessingTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DataProcessingPipelineInput input
    ) => Activate(input).Chain<ExecuteDataProcessingStep>().Resolve();
}
