using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.LookupPlayer.Steps;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

/// <summary>
/// Lightweight train for fast player profile lookups.
/// Designed to run directly on the API server via the GraphQL API.
/// Returns a typed LookupPlayerOutput with simulated profile data.
/// </summary>
[TraxQuery(Description = "Looks up a player profile")]
[TraxBroadcast]
public class LookupPlayerTrain
    : ServiceTrain<LookupPlayerInput, LookupPlayerOutput>,
        ILookupPlayerTrain
{
    protected override async Task<Either<Exception, LookupPlayerOutput>> RunInternal(
        LookupPlayerInput input
    ) => Activate(input).Chain<FetchPlayerStep>().Resolve();
}
