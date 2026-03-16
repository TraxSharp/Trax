using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards.Junctions;

public class CreditPlayersJunction(ILogger<CreditPlayersJunction> logger)
    : Junction<DistributeDailyRewardsInput, Unit>
{
    public override async Task<Unit> Run(DistributeDailyRewardsInput input)
    {
        logger.LogInformation(
            "Crediting {RewardType} rewards to 892 player accounts",
            input.RewardType
        );

        await Task.Delay(200);

        logger.LogInformation(
            "{RewardType} distribution complete — all players credited",
            input.RewardType
        );

        return Unit.Default;
    }
}
