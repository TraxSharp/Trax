using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Junctions;

public class UpdatePlayerStatsJunction(ILogger<UpdatePlayerStatsJunction> logger)
    : Junction<ProcessMatchResultInput, ProcessMatchResultInput>
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
