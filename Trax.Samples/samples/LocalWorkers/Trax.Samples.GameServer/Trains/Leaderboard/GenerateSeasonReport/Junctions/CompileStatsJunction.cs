using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport.Junctions;

public class CompileStatsJunction(ILogger<CompileStatsJunction> logger)
    : Junction<GenerateSeasonReportInput, GenerateSeasonReportInput>
{
    public override async Task<GenerateSeasonReportInput> Run(GenerateSeasonReportInput input)
    {
        logger.LogInformation("Compiling statistics for season {Season}", input.Season);

        await Task.Delay(200);

        logger.LogInformation(
            "Season {Season} stats compiled: 48,392 matches, 2,847 active players",
            input.Season
        );

        return input;
    }
}
