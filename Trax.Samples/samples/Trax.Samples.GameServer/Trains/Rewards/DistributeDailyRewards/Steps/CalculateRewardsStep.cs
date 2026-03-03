using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards.Steps;

public class CalculateRewardsStep(ILogger<CalculateRewardsStep> logger)
    : Step<DistributeDailyRewardsInput, DistributeDailyRewardsInput>
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
