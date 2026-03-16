using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Flowthru.Spaceflights.Trains.DataScience.Junctions;

namespace Trax.Samples.Flowthru.Spaceflights.Trains.DataScience;

/// <summary>
/// Wraps the flowthru DataScience pipeline as a Trax.Core ServiceTrain.
/// Splits data, trains a linear regression model, and evaluates predictions.
/// </summary>
public class DataScienceTrain : ServiceTrain<DataSciencePipelineInput, Unit>, IDataScienceTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DataSciencePipelineInput input
    ) => Activate(input).Chain<ExecuteDataScienceJunction>().Resolve();
}
