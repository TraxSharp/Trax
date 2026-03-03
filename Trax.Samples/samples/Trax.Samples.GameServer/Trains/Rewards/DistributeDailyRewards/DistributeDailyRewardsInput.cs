using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards;

public record DistributeDailyRewardsInput : IManifestProperties
{
    public required string RewardType { get; init; }
}
