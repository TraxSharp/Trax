using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.Steps;

public class RankPlayersStep(ILogger<RankPlayersStep> logger)
    : Step<RecalculateLeaderboardInput, Unit>
{
    public override async Task<Unit> Run(RecalculateLeaderboardInput input)
    {
        logger.LogInformation(
            "[{Region}] Ranking players and updating leaderboard positions",
            input.Region
        );

        await Task.Delay(200);

        logger.LogInformation(
            "[{Region}] Leaderboard recalculation complete — top player: xXDragonSlayerXx (rating: 2847)",
            input.Region
        );

        return Unit.Default;
    }
}
