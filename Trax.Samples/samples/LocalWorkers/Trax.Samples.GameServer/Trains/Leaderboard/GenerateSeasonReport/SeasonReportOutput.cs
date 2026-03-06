namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;

public record SeasonReportOutput
{
    public required string Season { get; init; }
    public required string ReportId { get; init; }
    public int TotalMatches { get; init; }
    public int ActivePlayers { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}
