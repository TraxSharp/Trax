using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

public class RunTriage(InvokedRun run, TicketBoard board) : EffectJunction<TriageCheck, CheckResult>
{
    public override async Task<CheckResult> Run(TriageCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken).ConfigureAwait(false);
        if (
            await board
                .RefuseUnlessHeld(ticket, run.UserKey, "triage", CancellationToken)
                .ConfigureAwait(false) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        if (!string.Equals(check.Area, ticket.NotNull().Report.Area, StringComparison.Ordinal))
            return CheckResult.Refuse(
                $"area {check.Area} does not own this report; it is {ticket.Report.Area}"
            );

        var moved = await board
            .Fire(
                ticket.Id,
                TicketTrigger.Triaged,
                new TriagedInput
                {
                    By = TicketBoard.Name(run.UserKey),
                    Note = $"area: {check.Area}",
                    Area = check.Area,
                },
                CancellationToken
            )
            .ConfigureAwait(false);
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept($"area {check.Area}; sent to Reproduce")
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
