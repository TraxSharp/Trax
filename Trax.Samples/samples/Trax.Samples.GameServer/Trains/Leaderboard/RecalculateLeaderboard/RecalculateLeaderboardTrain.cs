using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.Steps;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

/// <summary>
/// Recalculates the global leaderboard for a region.
/// Scheduled to run every 5 minutes on the scheduler.
/// GenerateSeasonReport depends on this train via ThenInclude.
/// </summary>
public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, Unit>,
        IRecalculateLeaderboardTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        RecalculateLeaderboardInput input
    ) => Activate(input).Chain<AggregateScoresStep>().Chain<RankPlayersStep>().Resolve();
}
