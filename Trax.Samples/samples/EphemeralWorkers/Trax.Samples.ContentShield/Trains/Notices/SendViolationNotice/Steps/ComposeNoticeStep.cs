using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Steps;

/// <summary>
/// Composes a violation notice from a template based on the violation type.
/// </summary>
public class ComposeNoticeStep(ILogger<ComposeNoticeStep> logger)
    : Step<SendViolationNoticeInput, SendViolationNoticeInput>
{
    public override async Task<SendViolationNoticeInput> Run(SendViolationNoticeInput input)
    {
        logger.LogInformation(
            "Composing {ViolationType} violation notice for content {ContentId}, user {UserId}",
            input.ViolationType,
            input.ContentId,
            input.UserId
        );

        await Task.Delay(100);

        logger.LogInformation(
            "Notice composed from template: violation-{ViolationType}",
            input.ViolationType
        );

        return input;
    }
}
