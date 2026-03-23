using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers.Junctions;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers;

/// <summary>
/// Hourly maintenance train that archives players inactive for more than N days.
/// Runs on the scheduler — never executed directly by the API.
/// </summary>
public class CleanupInactivePlayersTrain
    : ServiceTrain<CleanupInactivePlayersInput, Unit>,
        ICleanupInactivePlayersTrain
{
    protected override Unit Junctions() =>
        Chain<IdentifyInactiveJunction>().Chain<ArchivePlayersJunction>();
}
