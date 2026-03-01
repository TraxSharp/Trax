using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.Scheduler.Workflows.DataQualityCheck.Steps;

/// <summary>
/// Simulates analyzing data anomalies detected during extraction.
/// </summary>
public class AnalyzeAnomaliesStep(ILogger<AnalyzeAnomaliesStep> logger)
    : Step<DataQualityCheckInput, DataQualityCheckInput>
{
    public override async Task<DataQualityCheckInput> Run(DataQualityCheckInput input)
    {
        logger.LogInformation(
            "[{TableName}][Index {Index}] Analyzing {AnomalyCount} anomalies",
            input.TableName,
            input.Index,
            input.AnomalyCount
        );

        await Task.Delay(100);

        logger.LogInformation(
            "[{TableName}][Index {Index}] Anomaly analysis complete",
            input.TableName,
            input.Index
        );

        return input;
    }
}
