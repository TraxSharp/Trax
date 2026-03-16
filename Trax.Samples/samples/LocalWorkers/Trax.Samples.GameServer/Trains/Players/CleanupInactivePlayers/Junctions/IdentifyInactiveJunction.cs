using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers.Junctions;

public class IdentifyInactiveJunction(ILogger<IdentifyInactiveJunction> logger)
    : Junction<CleanupInactivePlayersInput, CleanupInactivePlayersInput>
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
