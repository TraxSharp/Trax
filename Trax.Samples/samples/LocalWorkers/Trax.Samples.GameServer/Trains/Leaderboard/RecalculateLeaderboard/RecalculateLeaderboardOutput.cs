namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

public record RecalculateLeaderboardOutput
{
    public required string Region { get; init; }
    public int PlayersProcessed { get; init; }
    public required string TopPlayer { get; init; }
    public int TopRating { get; init; }
}
