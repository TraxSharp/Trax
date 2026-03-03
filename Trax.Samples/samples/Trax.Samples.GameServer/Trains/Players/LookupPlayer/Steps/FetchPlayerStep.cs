using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer.Steps;

public class FetchPlayerStep(ILogger<FetchPlayerStep> logger) : Step<LookupPlayerInput, Unit>
{
    public override async Task<Unit> Run(LookupPlayerInput input)
    {
        logger.LogInformation(
            "Looking up player {PlayerId} — fetching profile from database",
            input.PlayerId
        );

        await Task.Delay(50);

        logger.LogInformation(
            "Player {PlayerId} found: rank=#42, wins=128, losses=64, rating=1847",
            input.PlayerId
        );

        return Unit.Default;
    }
}
