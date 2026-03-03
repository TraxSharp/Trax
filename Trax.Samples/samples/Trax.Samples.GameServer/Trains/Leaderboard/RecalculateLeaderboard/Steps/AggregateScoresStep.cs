using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.Steps;

public class AggregateScoresStep(ILogger<AggregateScoresStep> logger)
    : Step<RecalculateLeaderboardInput, RecalculateLeaderboardInput>
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
