using Trax.Core.Step;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Trax.Samples.Scheduler.Workflows.TransformLoad.Steps;

/// <summary>
/// Simulates loading the transformed data into the final destination.
/// </summary>
public class LoadDataStep(ILogger<LoadDataStep> logger) : Step<TransformLoadInput, Unit>
{
    public override async Task<Unit> Run(TransformLoadInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Loading data to final destination",
            input.TableName,
            input.Index
        );

        await Task.Delay(50);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Load complete",
            input.TableName,
            input.Index
        );

        return Unit.Default;
    }
}
