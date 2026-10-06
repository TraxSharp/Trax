using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice.Junctions;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;

/// <summary>
/// Sends a violation notice to the content owner. A moderator queues it through
/// <c>dispatch { sendViolationNotice }</c>; no other train starts it. Composes the notice
/// from a template and delivers it via email/push notification.
///
/// Dispatched to the ephemeral Runner via HTTP (UseRemoteWorkers).
/// </summary>
[TraxConcurrencyLimit(10)]
[TraxAuthorize(Roles = ContentShieldRoles.Moderator)]
[TraxMutation(
    GraphQLOperation.Queue,
    Description = "Sends a violation notice to the content owner"
)]
[TraxBroadcast]
public class SendViolationNoticeTrain
    : ServiceTrain<SendViolationNoticeInput, SendViolationNoticeOutput>,
        ISendViolationNoticeTrain
{
    protected override Task<Either<Exception, SendViolationNoticeOutput>> Junctions() =>
        Chain<ComposeNoticeJunction>().Chain<DeliverNoticeJunction>().Resolve();
}
