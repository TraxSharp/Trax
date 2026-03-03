using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport.Steps;

public class FormatReportStep(ILogger<FormatReportStep> logger)
    : Step<GenerateSeasonReportInput, Unit>
{
    public override async Task<Unit> Run(GenerateSeasonReportInput input)
    {
        logger.LogInformation("Formatting season {Season} report for distribution", input.Season);

        await Task.Delay(100);

        logger.LogInformation(
            "Season {Season} report generated and cached for API consumption",
            input.Season
        );

        return Unit.Default;
    }
}
