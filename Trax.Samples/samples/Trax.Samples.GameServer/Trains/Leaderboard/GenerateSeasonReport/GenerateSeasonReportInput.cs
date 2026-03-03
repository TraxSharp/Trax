using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;

public record GenerateSeasonReportInput : IManifestProperties
{
    public required string Season { get; init; }
}
