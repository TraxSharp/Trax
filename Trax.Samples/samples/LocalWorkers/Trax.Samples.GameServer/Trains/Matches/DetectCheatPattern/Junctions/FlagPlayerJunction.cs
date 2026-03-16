using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern.Junctions;

public class FlagPlayerJunction(ILogger<FlagPlayerJunction> logger)
    : Junction<DetectCheatPatternInput, Unit>
{
    public override async Task<Unit> Run(DetectCheatPatternInput input)
    {
        logger.LogWarning(
            "Flagging player {PlayerId} for review — {AnomalyCount} anomalies detected in match {MatchId}",
            input.PlayerId,
            input.AnomalyCount,
            input.MatchId
        );

        await Task.Delay(100);

        logger.LogInformation(
            "Player {PlayerId} flagged — queued for manual review by anti-cheat team",
            input.PlayerId
        );

        return Unit.Default;
    }
}
