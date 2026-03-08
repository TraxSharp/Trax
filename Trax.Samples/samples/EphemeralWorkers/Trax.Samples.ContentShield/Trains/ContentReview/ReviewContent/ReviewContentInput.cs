namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

public record ReviewContentInput
{
    public required string ContentId { get; init; }
    public required string ContentType { get; init; }
    public required string ContentBody { get; init; }
}
