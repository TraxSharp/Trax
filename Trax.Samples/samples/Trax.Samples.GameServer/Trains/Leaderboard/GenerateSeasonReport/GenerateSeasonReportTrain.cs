using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport.Steps;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;

/// <summary>
/// Generates a season standings report. Depends on RecalculateLeaderboard —
/// the scheduler only fires this after leaderboard recalculation completes.
/// </summary>
public class GenerateSeasonReportTrain
    : ServiceTrain<GenerateSeasonReportInput, Unit>,
        IGenerateSeasonReportTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        GenerateSeasonReportInput input
    ) => Activate(input).Chain<CompileStatsStep>().Chain<FormatReportStep>().Resolve();
}
