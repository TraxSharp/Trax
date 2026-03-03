using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards.Steps;

public class CreditPlayersStep(ILogger<CreditPlayersStep> logger)
    : Step<DistributeDailyRewardsInput, Unit>
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
