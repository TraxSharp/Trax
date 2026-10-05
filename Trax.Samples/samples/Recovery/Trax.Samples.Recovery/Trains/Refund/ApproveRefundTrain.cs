using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Refund.Junctions;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>
/// A refund approval: load the case, ask the model whether to pay it, pay, decline or queue it for a
/// person (whichever runs is the step the page can time out), then tell the customer. A retry after
/// that step times out takes the same track without asking the model again, unless the case changed.
/// </summary>
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
public class ApproveRefundTrain : ServiceTrain<RefundInput, RefundResult>, IApproveRefundTrain
{
    protected override Task<Either<Exception, RefundResult>> Junctions() =>
        Chain<LoadRefundCase>()
            .Gate<RefundCase, ApproveRefund>(gate =>
                gate.Yes(t => t.Chain<IssuePayment>(), atLeast: 0.8)
                    .No(t => t.Chain<DeclineRefund>(), below: 0.3)
                    .Unsure(t => t.Chain<QueueForReview>())
            )
            .Chain<NotifyCustomer>()
            .Resolve();
}
