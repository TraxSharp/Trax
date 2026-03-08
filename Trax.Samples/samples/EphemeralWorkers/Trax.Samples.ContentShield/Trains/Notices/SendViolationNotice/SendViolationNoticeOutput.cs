namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;

public record SendViolationNoticeOutput
{
    public required string NoticeId { get; init; }
    public DateTimeOffset DeliveredAt { get; init; }
}
