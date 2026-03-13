using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Steps;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;

/// <summary>
/// Sends a violation notice to the content owner. Dormant dependent — activated
/// by FlagContentStep when content is flagged. Composes the notice from a template
/// and delivers it via email/push notification.
///
/// Dispatched to the ephemeral Runner via HTTP (UseRemoteWorkers).
/// </summary>
[TraxConcurrencyLimit(10)]
[TraxMutation(
    GraphQLOperation.Queue,
    Description = "Sends a violation notice to the content owner"
)]
[TraxBroadcast]
public class SendViolationNoticeTrain
    : ServiceTrain<SendViolationNoticeInput, SendViolationNoticeOutput>,
        ISendViolationNoticeTrain
{
    protected override async Task<Either<Exception, SendViolationNoticeOutput>> RunInternal(
        SendViolationNoticeInput input
    ) => Activate(input).Chain<ComposeNoticeStep>().Chain<DeliverNoticeStep>().Resolve();
}
