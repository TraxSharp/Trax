using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.Scheduler.Trains.DataQualityCheck;
using Trax.Scheduler.Services.DormantDependentContext;
using static Trax.Samples.Scheduler.ManifestNames;

namespace Trax.Samples.Scheduler.Trains.ExtractImport.Steps;

/// <summary>
/// Simulates detecting data anomalies during extraction and conditionally
/// activating a dormant dependent DataQualityCheck train.
///
/// Demonstrates IDormantDependentContext: the quality check train is declared
/// in the topology (visible in the dashboard), but only fires when anomalies
/// are actually detected at runtime.
/// </summary>
public class CheckAndActivateQualityStep(
    IDormantDependentContext dormants,
    ILogger<CheckAndActivateQualityStep> logger
) : Step<ExtractImportInput, Unit>
{
    public override async Task<Unit> Run(ExtractImportInput input)
    {
        // Simulate anomaly detection — odd indexes have "anomalies"
        var anomalyCount = input.Index % 2 == 1 ? input.Index * 3 : 0;

        if (anomalyCount > 0)
        {
            logger.LogWarning(
                "[{TableName}][Index {Index}] Detected {AnomalyCount} anomalies — activating quality check",
                input.TableName,
                input.Index,
                anomalyCount
            );

            // Only the Transaction pipeline declares Dormant() quality checks.
            // The Customer pipeline uses regular chained dependents that auto-fire.
            if (input.TableName == ManifestNames.TransactionTable)
            {
                await dormants.ActivateAsync<IDataQualityCheckTrain, DataQualityCheckInput>(
                    ManifestNames.WithIndex(ManifestNames.DqTransaction, input.Index),
                    new DataQualityCheckInput
                    {
                        TableName = input.TableName,
                        Index = input.Index,
                        AnomalyCount = anomalyCount,
                    }
                );
            }
        }
        else
        {
            logger.LogInformation(
                "[{TableName}][Index {Index}] No anomalies detected — quality check skipped",
                input.TableName,
                input.Index
            );
        }

        return Unit.Default;
    }
}
