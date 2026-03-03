using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern.Steps;

public class AnalyzePatternStep(ILogger<AnalyzePatternStep> logger)
    : Step<DetectCheatPatternInput, DetectCheatPatternInput>
{
    public override async Task<DetectCheatPatternInput> Run(DetectCheatPatternInput input)
    {
        logger.LogWarning(
            "Analyzing {AnomalyCount} anomalies for player {PlayerId} in match {MatchId}",
            input.AnomalyCount,
            input.PlayerId,
            input.MatchId
        );

        await Task.Delay(500);

        logger.LogWarning(
            "Pattern analysis complete — confidence: {Confidence}%",
            Math.Min(input.AnomalyCount * 15, 99)
        );

        return input;
    }
}
