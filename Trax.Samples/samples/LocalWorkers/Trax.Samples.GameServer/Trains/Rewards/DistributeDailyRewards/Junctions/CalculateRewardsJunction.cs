using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards.Junctions;

public class CalculateRewardsJunction(ILogger<CalculateRewardsJunction> logger)
    : Junction<DistributeDailyRewardsInput, DistributeDailyRewardsInput>
{
    public override async Task<DistributeDailyRewardsInput> Run(DistributeDailyRewardsInput input)
    {
        logger.LogInformation(
            "Calculating {RewardType} rewards for all eligible players",
            input.RewardType
        );

        await Task.Delay(300);

        logger.LogInformation(
            "{RewardType} rewards calculated — 892 players eligible",
            input.RewardType
        );

        return input;
    }
}
