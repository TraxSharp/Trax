using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Players.BanPlayer;

public record BanPlayerInput : IManifestProperties
{
    public required string PlayerId { get; init; }
    public required string Reason { get; init; }
}
