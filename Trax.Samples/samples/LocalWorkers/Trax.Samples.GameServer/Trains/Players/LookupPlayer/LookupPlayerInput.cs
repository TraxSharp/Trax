using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

public record LookupPlayerInput : IManifestProperties
{
    public required string PlayerId { get; init; }
}
