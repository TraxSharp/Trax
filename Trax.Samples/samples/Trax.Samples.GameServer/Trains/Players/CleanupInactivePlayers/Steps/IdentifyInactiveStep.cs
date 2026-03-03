using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers.Steps;

public class IdentifyInactiveStep(ILogger<IdentifyInactiveStep> logger)
    : Step<CleanupInactivePlayersInput, CleanupInactivePlayersInput>
{
    public override async Task<CleanupInactivePlayersInput> Run(CleanupInactivePlayersInput input)
    {
        logger.LogInformation(
            "Scanning for players inactive for more than {InactiveDays} days",
            input.InactiveDays
        );

        await Task.Delay(200);

        logger.LogInformation("Found 23 inactive players to archive");

        return input;
    }
}
