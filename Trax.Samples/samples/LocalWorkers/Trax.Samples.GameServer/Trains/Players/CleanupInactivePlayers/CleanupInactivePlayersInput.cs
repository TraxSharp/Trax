using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers;

public record CleanupInactivePlayersInput : IManifestProperties
{
    public required int InactiveDays { get; init; }
}
