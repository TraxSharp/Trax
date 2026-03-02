using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Trains.ExtractImport.Steps;

/// <summary>
/// Validates the table name and index before extraction begins.
/// </summary>
public class ValidateTableStep(ILogger<ValidateTableStep> logger)
    : Step<ExtractImportInput, ExtractImportInput>
{
    public override async Task<ExtractImportInput> Run(ExtractImportInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Validating table configuration",
            input.TableName,
            input.Index
        );

        await Task.Delay(50);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Validation passed",
            input.TableName,
            input.Index
        );

        return input;
    }
}
