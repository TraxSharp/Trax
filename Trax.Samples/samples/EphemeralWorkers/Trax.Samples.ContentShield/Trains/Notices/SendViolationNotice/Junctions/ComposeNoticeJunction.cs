using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Junctions;

/// <summary>
/// Composes a violation notice from a template based on the violation type.
/// </summary>
public class ComposeNoticeJunction(ILogger<ComposeNoticeJunction> logger)
    : Junction<SendViolationNoticeInput, SendViolationNoticeInput>
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
