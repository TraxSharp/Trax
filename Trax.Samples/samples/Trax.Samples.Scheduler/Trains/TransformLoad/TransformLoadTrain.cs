using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.TransformLoad.Steps;

namespace Trax.Samples.Scheduler.Trains.TransformLoad;

/// <summary>
/// A "Transform &amp; Load" train that demonstrates dependent batch scheduling.
/// Runs after ExtractImportTrain succeeds via .ThenMany() chaining.
/// </summary>
public class TransformLoadTrain : ServiceTrain<TransformLoadInput, Unit>, ITransformLoadTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(TransformLoadInput input) =>
        Activate(input).Chain<TransformDataStep>().Chain<LoadDataStep>().Resolve();
}
