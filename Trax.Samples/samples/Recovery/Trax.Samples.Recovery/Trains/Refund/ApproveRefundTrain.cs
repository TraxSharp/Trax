using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Refund.Junctions;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>
/// A refund request: load the case, ask the model what went wrong and check it, ask the model whether
/// to pay, then pay, decline or queue it for a person, save the outcome, and tell the customer. The page
/// can crash the run while it pays or while it emails. A crash while paying is retried from the top
/// with both answers replayed; a crash while emailing is retried from the checkpoint, so the payment is
/// not made twice.
/// </summary>
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
public class ApproveRefundTrain : ServiceTrain<RefundInput, RefundResult>, IApproveRefundTrain
{
    protected override Task<Either<Exception, RefundResult>> Junctions() =>
        Chain<LoadRefundCase>()
            // Ask the model: what went wrong with this order?
            .Switch<RefundCase, Problem>(problem =>
                problem
                    .When(Problem.Damaged, then => then.Chain<RequestPhotos>())
                    .When(Problem.Lost, then => then.Chain<TraceParcel>())
                    .When(Problem.Unwanted, then => then.Chain<CheckReturnWindow>())
            )
            // Ask the model: should we pay it?
            .Gate<RefundEvidence, ApproveRefund>(answer =>
                answer
                    .Yes(then => then.Chain<IssuePayment>(), atLeast: 0.8)
                    .No(then => then.Chain<DeclineRefund>(), below: 0.3)
                    .Unsure(then => then.Chain<QueueForReview>())
            )
            // Save progress: a retry starts here, so nobody is paid twice.
            .Checkpoint<RefundOutcome>()
            .Chain<NotifyCustomer>()
            .Resolve();
}
