using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.LookupPlayer.Junctions;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

/// <summary>
/// Lightweight train for fast player profile lookups.
/// Designed to run directly on the API server via the GraphQL API.
/// </summary>
[TraxQuery(Description = "Looks up a player profile")]
[TraxBroadcast]
public class LookupPlayerTrain : ServiceTrain<LookupPlayerInput, PlayerProfile>, ILookupPlayerTrain
{
    protected override async Task<Either<Exception, PlayerProfile>> RunInternal(
        LookupPlayerInput input
    ) => Activate(input).Chain<FetchPlayerJunction>().Resolve();
}
