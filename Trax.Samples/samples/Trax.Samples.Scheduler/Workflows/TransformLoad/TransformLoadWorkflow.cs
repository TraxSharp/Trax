using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Workflows.TransformLoad.Steps;

namespace Trax.Samples.Scheduler.Workflows.TransformLoad;

/// <summary>
/// A "Transform &amp; Load" workflow that demonstrates dependent batch scheduling.
/// Runs after ExtractImportWorkflow succeeds via .ThenMany() chaining.
/// </summary>
public class TransformLoadWorkflow : ServiceTrain<TransformLoadInput, Unit>, ITransformLoadWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(TransformLoadInput input) =>
        Activate(input).Chain<TransformDataStep>().Chain<LoadDataStep>().Resolve();
}
