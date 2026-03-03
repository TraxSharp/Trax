using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers.Steps;

public class ArchivePlayersStep(ILogger<ArchivePlayersStep> logger)
    : Step<CleanupInactivePlayersInput, Unit>
{
    public override async Task<Unit> Run(CleanupInactivePlayersInput input)
    {
        logger.LogInformation("Archiving 23 inactive players and freeing resources");

        await Task.Delay(150);

        logger.LogInformation("Player cleanup complete — 23 players archived");

        return Unit.Default;
    }
}
