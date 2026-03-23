using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport.Junctions;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;

/// <summary>
/// Generates a season standings report. Depends on RecalculateLeaderboard —
/// the scheduler only fires this after leaderboard recalculation completes.
/// </summary>
public class GenerateSeasonReportTrain
    : ServiceTrain<GenerateSeasonReportInput, SeasonReportOutput>,
        IGenerateSeasonReportTrain
{
    protected override SeasonReportOutput Junctions() =>
        Chain<CompileStatsJunction>().Chain<FormatReportJunction>();
}
