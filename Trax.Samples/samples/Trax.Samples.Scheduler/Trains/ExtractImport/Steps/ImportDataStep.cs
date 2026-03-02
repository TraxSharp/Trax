using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Trains.ExtractImport.Steps;

/// <summary>
/// Simulates importing the extracted data into the destination.
/// </summary>
public class ImportDataStep(ILogger<ImportDataStep> logger) : Step<ExtractImportInput, Unit>
{
    public override async Task<Unit> Run(ExtractImportInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Importing data to destination",
            input.TableName,
            input.Index
        );

        await Task.Delay(75);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Import complete",
            input.TableName,
            input.Index
        );

        return Unit.Default;
    }
}
