using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience.Steps;
using LanguageExt;

namespace Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience;

/// <summary>
/// Wraps the flowthru DataScience pipeline as a Trax.Core ServiceTrain.
/// Splits data, trains a linear regression model, and evaluates predictions.
/// </summary>
public class DataSciencePipelineWorkflow
    : ServiceTrain<DataSciencePipelineInput, Unit>,
        IDataSciencePipelineWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DataSciencePipelineInput input
    ) => Activate(input).Chain<ExecuteDataScienceStep>().Resolve();
}
