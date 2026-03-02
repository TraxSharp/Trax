using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Trains.TransformLoad.Steps;

/// <summary>
/// Simulates transforming extracted data before loading.
/// </summary>
public class TransformDataStep(ILogger<TransformDataStep> logger)
    : Step<TransformLoadInput, TransformLoadInput>
{
    public override async Task<TransformLoadInput> Run(TransformLoadInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Transforming extracted data",
            input.TableName,
            input.Index
        );

        await Task.Delay(50);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Transform complete",
            input.TableName,
            input.Index
        );

        return input;
    }
}
