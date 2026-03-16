using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.Junctions;

public class AggregateScoresJunction(ILogger<AggregateScoresJunction> logger)
    : Junction<RecalculateLeaderboardInput, RecalculateLeaderboardInput>
{
    public override async Task<RecalculateLeaderboardInput> Run(RecalculateLeaderboardInput input)
    {
        logger.LogInformation(
            "[{Region}] Aggregating player scores from recent matches",
            input.Region
        );

        await Task.Delay(300);

        logger.LogInformation(
            "[{Region}] Score aggregation complete — 1,247 players processed",
            input.Region
        );

        return input;
    }
}
