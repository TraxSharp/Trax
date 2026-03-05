namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

public record ProcessMatchResultOutput
{
    public required string MatchId { get; init; }
    public required string Region { get; init; }
    public required string WinnerId { get; init; }
    public required string LoserId { get; init; }
    public required int WinnerNewRating { get; init; }
    public required int LoserNewRating { get; init; }
    public required bool AnomalyDetected { get; init; }
}
