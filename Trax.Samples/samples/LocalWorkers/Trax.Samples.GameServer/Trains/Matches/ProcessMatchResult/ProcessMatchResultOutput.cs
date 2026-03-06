namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

public record ProcessMatchResultOutput
{
    public required string MatchId { get; init; }
    public required string Region { get; init; }
    public required string WinnerId { get; init; }
    public required string LoserId { get; init; }
    public int WinnerNewRating { get; init; }
    public int LoserNewRating { get; init; }
    public int AnomaliesDetected { get; init; }
    public bool CheatDetectionTriggered { get; init; }
}
