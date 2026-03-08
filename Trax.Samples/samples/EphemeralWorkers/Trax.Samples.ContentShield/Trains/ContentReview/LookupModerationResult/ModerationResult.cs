namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult;

public record ModerationResult
{
    public required string ContentId { get; init; }
    public required string ModerationStatus { get; init; }
    public required string Classification { get; init; }
    public double ThreatScore { get; init; }
    public DateTimeOffset ReviewedAt { get; init; }
}
