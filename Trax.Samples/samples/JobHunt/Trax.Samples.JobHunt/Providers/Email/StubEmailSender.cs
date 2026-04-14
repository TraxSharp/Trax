using Microsoft.Extensions.Logging;

namespace Trax.Samples.JobHunt.Providers.Email;

/// <summary>
/// Stub email sender for v1. Logs the send and returns a fake message ID.
/// Replace with SmtpEmailSender or PostmarkEmailSender in a later phase.
/// </summary>
public class StubEmailSender(ILogger<StubEmailSender> logger) : IEmailSender
{
    public Task<SendEmailResult> SendAsync(
        string to,
        string subject,
        string body,
        string? attachmentPath,
        CancellationToken ct = default
    )
    {
        var messageId = $"stub-{Guid.NewGuid():N}";

        logger.LogInformation(
            "[DRY RUN] Would send email to {To}, subject: {Subject}, messageId: {MessageId}",
            to,
            subject,
            messageId
        );

        return Task.FromResult(new SendEmailResult(messageId, "Stub"));
    }
}
