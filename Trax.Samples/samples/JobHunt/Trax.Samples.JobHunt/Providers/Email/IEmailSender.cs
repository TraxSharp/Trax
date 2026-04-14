namespace Trax.Samples.JobHunt.Providers.Email;

public record SendEmailResult(string MessageId, string Provider);

public interface IEmailSender
{
    Task<SendEmailResult> SendAsync(
        string to,
        string subject,
        string body,
        string? attachmentPath,
        CancellationToken ct = default
    );
}
