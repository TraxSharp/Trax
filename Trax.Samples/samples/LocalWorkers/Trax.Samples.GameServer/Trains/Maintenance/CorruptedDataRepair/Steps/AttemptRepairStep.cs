using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair.Steps;

/// <summary>
/// Always throws — simulates a corrupted data repair that can't be completed automatically.
/// This provides a realistic failure with a meaningful stack trace for the dead letter detail page.
/// </summary>
public class AttemptRepairStep(ILogger<AttemptRepairStep> logger)
    : Step<CorruptedDataRepairInput, Unit>
{
    public override async Task<Unit> Run(CorruptedDataRepairInput input)
    {
        logger.LogWarning(
            "Attempting to repair corrupted data in table '{TableName}'...",
            input.TableName
        );

        await Task.Delay(50);

        throw new InvalidOperationException(
            $"Automated repair failed for table '{input.TableName}': "
                + "data corruption too severe for automatic recovery. "
                + "Manual intervention required — see dead letter detail page."
        );
    }
}
