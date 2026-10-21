using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

public interface IReviewTrain : IServiceTrain<ReviewCheck, CheckResult>;

/// <summary>Records a reviewer's decision: an approval closes the ticket as done, a send-back returns it to Fix.</summary>
[TraxAuthorize(Roles = BoardRoles.Reviewer)]
public class ReviewTrain : ServiceTrain<ReviewCheck, CheckResult>, IReviewTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RecordReview>().Resolve();
}

public class RecordReview(InvokedRun run, TicketBoard board)
    : EffectJunction<ReviewCheck, CheckResult>
{
    public override async Task<CheckResult> Run(ReviewCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "review", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var approve = check.Decision == "approve";
        var moved = await board.Fire(
            ticket!.Id,
            approve ? TicketTrigger.Approved : TicketTrigger.SentBack,
            new NoteInput
            {
                By = TicketBoard.Name(run.UserKey),
                Note = approve
                    ? $"approved by {TicketBoard.Name(run.UserKey)}"
                    : $"sent back: {check.Reason}",
            },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept(approve ? "approved; done" : "sent back to Fix")
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
