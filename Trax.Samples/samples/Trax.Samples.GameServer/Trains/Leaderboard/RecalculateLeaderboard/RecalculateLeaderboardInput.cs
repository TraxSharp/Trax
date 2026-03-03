using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

public record RecalculateLeaderboardInput : IManifestProperties
{
    public required string Region { get; init; }
}
