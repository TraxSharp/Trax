namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;

public record SendViolationNoticeInput
{
    public required string ContentId { get; init; }
    public required string ViolationType { get; init; }
    public required string UserId { get; init; }
}
