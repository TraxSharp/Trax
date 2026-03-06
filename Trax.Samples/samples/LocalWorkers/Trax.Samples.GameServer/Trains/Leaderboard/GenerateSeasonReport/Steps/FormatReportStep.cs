using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport.Steps;

public class FormatReportStep(ILogger<FormatReportStep> logger)
    : Step<GenerateSeasonReportInput, SeasonReportOutput>
{
    public override async Task<SeasonReportOutput> Run(GenerateSeasonReportInput input)
    {
        logger.LogInformation("Formatting season {Season} report for distribution", input.Season);

        await Task.Delay(100);

        logger.LogInformation(
            "Season {Season} report generated and cached for API consumption",
            input.Season
        );

        return new SeasonReportOutput
        {
            Season = input.Season,
            ReportId = $"report-{input.Season}-{Guid.NewGuid().ToString("N")[..8]}",
            TotalMatches = 48392,
            ActivePlayers = 2847,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }
}
