using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>Records a reviewer's decision: an approval closes the ticket as done, a send-back returns it to Fix.</summary>
[TraxAuthorize(Roles = BoardRoles.Reviewer)]
public class ReviewTrain : ServiceTrain<ReviewCheck, CheckResult>, IReviewTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RecordReview>().Resolve();
}
