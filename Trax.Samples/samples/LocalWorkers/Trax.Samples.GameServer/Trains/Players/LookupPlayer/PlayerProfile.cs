namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

public record PlayerProfile
{
    public required string PlayerId { get; init; }
    public int Rank { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Rating { get; init; }
}
