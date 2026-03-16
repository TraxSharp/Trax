using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Players.BanPlayer.Junctions;

public class ApplyBanJunction(ILogger<ApplyBanJunction> logger) : Junction<BanPlayerInput, Unit>
{
    public override async Task<Unit> Run(BanPlayerInput input)
    {
        logger.LogWarning(
            "BANNING player {PlayerId} — reason: {Reason}",
            input.PlayerId,
            input.Reason
        );

        await Task.Delay(100);

        logger.LogInformation(
            "Player {PlayerId} has been banned. Session invalidated, matchmaking disabled.",
            input.PlayerId
        );

        return Unit.Default;
    }
}
