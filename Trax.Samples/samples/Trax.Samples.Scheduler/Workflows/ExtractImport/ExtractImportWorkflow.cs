using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Workflows.ExtractImport.Steps;

namespace Trax.Samples.Scheduler.Workflows.ExtractImport;

/// <summary>
/// Simulates an extract-import pipeline for a given table and index partition.
/// Validates the table, extracts data from the source, imports it to the destination,
/// and conditionally activates a dormant dependent quality check when anomalies are detected.
/// </summary>
public class ExtractImportWorkflow : ServiceTrain<ExtractImportInput, Unit>, IExtractImportWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(ExtractImportInput input) =>
        Activate(input)
            .Chain<ValidateTableStep>()
            .Chain<ExtractDataStep>()
            .Chain<ImportDataStep>()
            .Chain<CheckAndActivateQualityStep>()
            .Resolve();
}
