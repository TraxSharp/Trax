using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;
using static Trax.Samples.Board.Shop.ShippingRules;

namespace Trax.Samples.Board.Trains.Checks;

public interface IQaTrain : IServiceTrain<QaCheck, CheckResult>;

/// <summary>
/// Runs QA's own cases on the rules as patched. QA's work is accepted either way: a case that breaks sends the ticket
/// back to Fix with it, and a clean sweep sends it on to Review.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Qa)]
public class QaTrain : ServiceTrain<QaCheck, CheckResult>, IQaTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunQaSweep>().Resolve();
}

public class RunQaSweep(InvokedRun run, TicketBoard board) : EffectJunction<QaCheck, CheckResult>
{
    public override async Task<CheckResult> Run(QaCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "qa", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var report = ticket!.Report;
        var patched = report.Current.With(
            ticket.Context.FixFreeFrom ?? report.Current.FreeFrom,
            ticket.Context.FixInclusive ?? report.Current.Inclusive
        );
        var broken = check
            .Subtotals.Where(s => patched.Charge(s) != report.Policy.Charge(s))
            .ToList();

        var (trigger, note, message) =
            broken.Count == 0
                ? (
                    TicketTrigger.QaPassed,
                    $"qa: {check.Subtotals.Count} of {check.Subtotals.Count} pass",
                    $"{check.Subtotals.Count} of {check.Subtotals.Count} pass on the patched rules; sent to Review"
                )
                : (
                    TicketTrigger.QaFailed,
                    $"qa: {Money(broken[0])} pays {Money(patched.Charge(broken[0]))}",
                    $"{Money(broken[0])} pays {Money(patched.Charge(broken[0]))} on the patched rules; the policy says {Money(report.Policy.Charge(broken[0]))}; sent back to Fix"
                );

        var moved = await board.Fire(
            ticket.Id,
            trigger,
            new NoteInput { By = TicketBoard.Name(run.UserKey), Note = note },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept(message)
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
