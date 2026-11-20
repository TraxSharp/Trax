using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

public class RecordReview(InvokedRun run, TicketBoard board)
    : EffectJunction<ReviewCheck, CheckResult>
{
    public override async Task<CheckResult> Run(ReviewCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken).ConfigureAwait(false);
        if (
            await board
                .RefuseUnlessHeld(ticket, run.UserKey, "review", CancellationToken)
                .ConfigureAwait(false) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var approve = string.Equals(check.Decision, "approve", StringComparison.Ordinal);
        var moved = await board
            .Fire(
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
            )
            .ConfigureAwait(false);
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept(approve ? "approved; done" : "sent back to Fix")
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
