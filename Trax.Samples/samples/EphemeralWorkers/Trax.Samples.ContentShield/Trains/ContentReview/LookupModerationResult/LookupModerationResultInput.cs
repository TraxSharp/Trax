namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult;

public record LookupModerationResultInput
{
    public required string ContentId { get; init; }
}
