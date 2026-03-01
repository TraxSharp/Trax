using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Workflows.ExtractImport.Steps;

/// <summary>
/// Simulates extracting data from the source table at the given index.
/// </summary>
public class ExtractDataStep(ILogger<ExtractDataStep> logger)
    : Step<ExtractImportInput, ExtractImportInput>
{
    public override async Task<ExtractImportInput> Run(ExtractImportInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Extracting data from source",
            input.TableName,
            input.Index
        );

        await Task.Delay(5000);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Extraction complete",
            input.TableName,
            input.Index
        );

        return input;
    }
}
