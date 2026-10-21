using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

public interface ITriageTrain : IServiceTrain<TriageCheck, CheckResult>;

/// <summary>Accepts a triage when the area it names is the area of the report, and sends the ticket to Reproduce.</summary>
[TraxAuthorize(Roles = BoardRoles.Triage)]
public class TriageTrain : ServiceTrain<TriageCheck, CheckResult>, ITriageTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunTriage>().Resolve();
}

public class RunTriage(InvokedRun run, TicketBoard board) : EffectJunction<TriageCheck, CheckResult>
{
    public override async Task<CheckResult> Run(TriageCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "triage", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        if (!string.Equals(check.Area, ticket!.Report.Area, StringComparison.Ordinal))
            return CheckResult.Refuse(
                $"area {check.Area} does not own this report; it is {ticket.Report.Area}"
            );

        var moved = await board.Fire(
            ticket.Id,
            TicketTrigger.Triaged,
            new TriagedInput
            {
                By = TicketBoard.Name(run.UserKey),
                Note = $"area: {check.Area}",
                Area = check.Area,
            },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept($"area {check.Area}; sent to Reproduce")
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
