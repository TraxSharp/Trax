using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Workflows.DataQualityCheck.Steps;

/// <summary>
/// Simulates reporting the results of a data quality analysis.
/// </summary>
public class ReportResultsStep(ILogger<ReportResultsStep> logger)
    : Step<DataQualityCheckInput, Unit>
{
    public override async Task<Unit> Run(DataQualityCheckInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Reporting {AnomalyCount} anomalies to quality dashboard",
            input.TableName,
            input.Index,
            input.AnomalyCount
        );

        await Task.Delay(50);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Quality report submitted",
            input.TableName,
            input.Index
        );

        return Unit.Default;
    }
}
