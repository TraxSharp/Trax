using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards.Steps;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards;

/// <summary>
/// Distributes daily login rewards to all active players.
/// Scheduled via Cron.Daily(hour: 0) — runs once daily at midnight.
/// </summary>
public class DistributeDailyRewardsTrain
    : ServiceTrain<DistributeDailyRewardsInput, Unit>,
        IDistributeDailyRewardsTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        DistributeDailyRewardsInput input
    ) => Activate(input).Chain<CalculateRewardsStep>().Chain<CreditPlayersStep>().Resolve();
}
