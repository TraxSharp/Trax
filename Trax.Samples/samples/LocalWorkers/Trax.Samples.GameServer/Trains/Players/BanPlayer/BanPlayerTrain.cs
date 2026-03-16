using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.BanPlayer.Junctions;

namespace Trax.Samples.GameServer.Trains.Players.BanPlayer;

/// <summary>
/// Admin-only train for banning players.
/// Requires the "Admin" authorization policy — players with only the "Player" role get 403.
/// </summary>
[TraxAuthorize("Admin")]
[TraxMutation(GraphQLOperation.Run, Description = "Bans a player (admin only)")]
public class BanPlayerTrain : ServiceTrain<BanPlayerInput, Unit>, IBanPlayerTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(BanPlayerInput input) =>
        Activate(input).Chain<ApplyBanJunction>().Resolve();
}
