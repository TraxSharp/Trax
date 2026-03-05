using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern;
using Trax.Scheduler.Services.DormantDependentContext;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Steps;

/// <summary>
/// Checks for suspicious patterns in match results.
/// When anomalies are detected, activates the dormant DetectCheatPattern train
/// via IDormantDependentContext — demonstrating runtime-activated dependents.
/// Returns a ProcessMatchResultOutput with the match processing summary.
/// </summary>
public class CheckForAnomaliesStep(
    IDormantDependentContext dormants,
    ILogger<CheckForAnomaliesStep> logger
) : Step<ProcessMatchResultInput, ProcessMatchResultOutput>
{
    public override async Task<ProcessMatchResultOutput> Run(ProcessMatchResultInput input)
    {
        // Simulate anomaly detection — score differences > 50 are "suspicious"
        var scoreDiff = Math.Abs(input.WinnerScore - input.LoserScore);
        var anomalyCount = scoreDiff > 50 ? scoreDiff / 10 : 0;
        var anomalyDetected = anomalyCount > 0;

        if (anomalyDetected)
        {
            logger.LogWarning(
                "[{Region}] Detected {AnomalyCount} anomalies in match {MatchId} — activating cheat detection",
                input.Region,
                anomalyCount,
                input.MatchId
            );

            await dormants.ActivateAsync<IDetectCheatPatternTrain, DetectCheatPatternInput>(
                ManifestNames.WithIndex(ManifestNames.DetectCheat, input.Region),
                new DetectCheatPatternInput
                {
                    PlayerId = input.WinnerId,
                    MatchId = input.MatchId,
                    AnomalyCount = anomalyCount,
                }
            );
        }
        else
        {
            logger.LogInformation(
                "[{Region}] No anomalies detected in match {MatchId} — cheat detection skipped",
                input.Region,
                input.MatchId
            );
        }

        // Simulate ELO rating changes
        var winnerNewRating = 1500 + input.WinnerScore;
        var loserNewRating = 1500 - input.LoserScore;

        return new ProcessMatchResultOutput
        {
            MatchId = input.MatchId,
            Region = input.Region,
            WinnerId = input.WinnerId,
            LoserId = input.LoserId,
            WinnerNewRating = winnerNewRating,
            LoserNewRating = loserNewRating,
            AnomalyDetected = anomalyDetected,
        };
    }
}
