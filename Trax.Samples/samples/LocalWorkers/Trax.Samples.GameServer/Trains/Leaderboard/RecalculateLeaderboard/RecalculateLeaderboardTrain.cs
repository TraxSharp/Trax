using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.Junctions;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

/// <summary>
/// Recalculates the global leaderboard for a region.
/// Scheduled to run every 5 minutes on the scheduler.
/// GenerateSeasonReport depends on this train via ThenInclude.
/// </summary>
[TraxMutation(
    GraphQLOperation.Queue,
    Namespace = GraphQLNamespaces.Leaderboard,
    Description = "Recalculates the leaderboard"
)]
public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain
{
    protected override RecalculateLeaderboardOutput Junctions() =>
        Chain<AggregateScoresJunction>().Chain<RankPlayersJunction>();
}
