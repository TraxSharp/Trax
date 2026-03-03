using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.LookupPlayer.Steps;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

/// <summary>
/// Lightweight train for fast player profile lookups.
/// Designed to run directly on the API server via POST /trains/run.
/// </summary>
public class LookupPlayerTrain : ServiceTrain<LookupPlayerInput, Unit>, ILookupPlayerTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(LookupPlayerInput input) =>
        Activate(input).Chain<FetchPlayerStep>().Resolve();
}
