using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.DataQualityCheck.Steps;

namespace Trax.Samples.Scheduler.Trains.DataQualityCheck;

/// <summary>
/// A data quality check train that runs only when the parent ExtractImport
/// detects anomalies. Demonstrates dormant dependent scheduling — declared in
/// the topology but only activated at runtime with context-specific input.
/// </summary>
public class DataQualityCheckTrain
    : ServiceTrain<DataQualityCheckInput, Unit>,
        IDataQualityCheckTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DataQualityCheckInput input
    ) => Activate(input).Chain<AnalyzeAnomaliesStep>().Chain<ReportResultsStep>().Resolve();
}
