using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

public interface ICloseTrain : IServiceTrain<CloseCheck, CheckResult>;

/// <summary>
/// Closes a ticket nobody could reproduce. The ticket's own guard decides: it counts the cases the server ran, not the
/// ones the agent says it tried.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Reproducer)]
public class CloseTrain : ServiceTrain<CloseCheck, CheckResult>, ICloseTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunClose>().Resolve();
}

public class RunClose(InvokedRun run, TicketBoard board) : EffectJunction<CloseCheck, CheckResult>
{
    public override async Task<CheckResult> Run(CloseCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "reproduce", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var failed = ticket!.Context.FailedCases ?? 0;
        var moved = await board.Fire(
            ticket.Id,
            TicketTrigger.CannotReproduce,
            new NoteInput
            {
                By = TicketBoard.Name(run.UserKey),
                Note = $"cannot reproduce: {failed} cases matched the policy",
            },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept($"closed: {failed} cases matched the policy", failed)
            : CheckResult.Refuse(TicketBoard.Why(moved), failed);
    }
}
