using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards;

public interface IDistributeDailyRewardsTrain : IServiceTrain<DistributeDailyRewardsInput, Unit>;
