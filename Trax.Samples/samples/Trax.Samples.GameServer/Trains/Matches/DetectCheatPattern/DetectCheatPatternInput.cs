using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern;

public record DetectCheatPatternInput : IManifestProperties
{
    public required string PlayerId { get; init; }
    public required string MatchId { get; init; }
    public required int AnomalyCount { get; init; }
}
