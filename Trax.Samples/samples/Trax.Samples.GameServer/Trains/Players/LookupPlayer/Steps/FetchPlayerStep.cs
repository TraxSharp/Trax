using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer.Steps;

public class FetchPlayerStep(ILogger<FetchPlayerStep> logger)
    : Step<LookupPlayerInput, LookupPlayerOutput>
{
    public override async Task<LookupPlayerOutput> Run(LookupPlayerInput input)
    {
        logger.LogInformation(
            "Looking up player {PlayerId} — fetching profile from database",
            input.PlayerId
        );

        await Task.Delay(50);

        var output = new LookupPlayerOutput
        {
            PlayerId = input.PlayerId,
            Rank = 42,
            Wins = 128,
            Losses = 64,
            Rating = 1847,
        };

        logger.LogInformation(
            "Player {PlayerId} found: rank=#{Rank}, wins={Wins}, losses={Losses}, rating={Rating}",
            output.PlayerId,
            output.Rank,
            output.Wins,
            output.Losses,
            output.Rating
        );

        return output;
    }
}
