namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

public record ReviewContentOutput
{
    public required string ContentId { get; init; }
    public required string Classification { get; init; }
    public double ThreatScore { get; init; }
    public bool IsFlagged { get; init; }
    public string? FlagReason { get; init; }
}
