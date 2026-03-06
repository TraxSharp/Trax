using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

public record ProcessMatchResultInput : IManifestProperties
{
    public required string Region { get; init; }
    public required string MatchId { get; init; }
    public required string WinnerId { get; init; }
    public required string LoserId { get; init; }
    public int WinnerScore { get; init; }
    public int LoserScore { get; init; }
}
