using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers.Junctions;

public class ArchivePlayersJunction(ILogger<ArchivePlayersJunction> logger)
    : Junction<CleanupInactivePlayersInput, Unit>
{
    public override async Task<Unit> Run(CleanupInactivePlayersInput input)
    {
        logger.LogInformation("Archiving 23 inactive players and freeing resources");

        await Task.Delay(150);

        logger.LogInformation("Player cleanup complete — 23 players archived");

        return Unit.Default;
    }
}
