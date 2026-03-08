using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Steps;

/// <summary>
/// Delivers the composed violation notice via email and push notification.
/// </summary>
public class DeliverNoticeStep(ILogger<DeliverNoticeStep> logger)
    : Step<SendViolationNoticeInput, SendViolationNoticeOutput>
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
