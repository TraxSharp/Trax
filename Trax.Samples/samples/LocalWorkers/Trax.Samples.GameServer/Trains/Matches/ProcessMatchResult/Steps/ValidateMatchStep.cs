using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Steps;

public class ValidateMatchStep(ILogger<ValidateMatchStep> logger)
    : Step<ProcessMatchResultInput, ProcessMatchResultInput>
{
    public override async Task<ProcessMatchResultInput> Run(ProcessMatchResultInput input)
    {
        logger.LogInformation(
            "[{Region}] Validating match {MatchId}: {WinnerId} vs {LoserId} ({WinnerScore}-{LoserScore})",
            input.Region,
            input.MatchId,
            input.WinnerId,
            input.LoserId,
            input.WinnerScore,
            input.LoserScore
        );

        await Task.Delay(100);

        logger.LogInformation("[{Region}] Match {MatchId} validated", input.Region, input.MatchId);

        return input;
    }
}
