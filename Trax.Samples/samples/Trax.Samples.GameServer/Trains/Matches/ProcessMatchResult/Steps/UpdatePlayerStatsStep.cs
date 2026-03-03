using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Steps;

public class UpdatePlayerStatsStep(ILogger<UpdatePlayerStatsStep> logger)
    : Step<ProcessMatchResultInput, ProcessMatchResultInput>
{
    public override async Task<ProcessMatchResultInput> Run(ProcessMatchResultInput input)
    {
        logger.LogInformation(
            "[{Region}] Updating stats for {WinnerId} (winner) and {LoserId} (loser)",
            input.Region,
            input.WinnerId,
            input.LoserId
        );

        await Task.Delay(200);

        logger.LogInformation(
            "[{Region}] Player stats updated — ELO ratings recalculated",
            input.Region
        );

        return input;
    }
}
