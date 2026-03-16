using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Junctions;

/// <summary>
/// Delivers the composed violation notice via email and push notification.
/// </summary>
public class DeliverNoticeJunction(ILogger<DeliverNoticeJunction> logger)
    : Junction<SendViolationNoticeInput, SendViolationNoticeOutput>
{
    public override async Task<SendViolationNoticeOutput> Run(SendViolationNoticeInput input)
    {
        logger.LogInformation(
            "Delivering violation notice to user {UserId} for content {ContentId}",
            input.UserId,
            input.ContentId
        );

        await Task.Delay(150);

        var noticeId = $"notice-{Guid.NewGuid():N}";
        logger.LogInformation("Notice {NoticeId} delivered successfully", noticeId);

        return new SendViolationNoticeOutput
        {
            NoticeId = noticeId,
            DeliveredAt = DateTimeOffset.UtcNow,
        };
    }
}
