namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

public record LookupPlayerOutput
{
    public required string PlayerId { get; init; }
    public required int Rank { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required int Rating { get; init; }
}
