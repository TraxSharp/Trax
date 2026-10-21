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

public interface IVerifyFixTrain : IServiceTrain<FixCheck, CheckResult>;

/// <summary>
/// Applies a fixer's patch to the shop's rules and runs the ticket's repro case and every earlier case on the result.
/// The patch is accepted only if each one now charges what the policy says, and the ticket goes to QA.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Fixer)]
public class VerifyFixTrain : ServiceTrain<FixCheck, CheckResult>, IVerifyFixTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunFixCheck>().Resolve();
}

public class RunFixCheck(InvokedRun run, TicketBoard board) : EffectJunction<FixCheck, CheckResult>
{
    public override async Task<CheckResult> Run(FixCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "fix", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var report = ticket!.Report;
        var patched = report.Current.With(check.FreeFrom, check.Inclusive);
        var cases = report
            .EarlierCases.Prepend(ticket.Context.ReproSubtotal ?? report.Policy.FreeFrom)
            .Distinct()
            .ToList();
        var broken = cases.FirstOrDefault(s => patched.Charge(s) != report.Policy.Charge(s), -1m);

        if (broken >= 0)
        {
            var why =
                $"{Money(broken)} pays {Money(patched.Charge(broken))} with this patch; the policy says {Money(report.Policy.Charge(broken))}";
            await board.Fire(
                ticket.Id,
                TicketTrigger.FixRejected,
                new NoteInput
                {
                    By = TicketBoard.Name(run.UserKey),
                    Note = $"patch refused: {why}",
                },
                CancellationToken
            );
            return CheckResult.Refuse(why);
        }

        var patch = $"free from {Money(check.FreeFrom)}{(check.Inclusive ? ", inclusive" : "")}";
        var moved = await board.Fire(
            ticket.Id,
            TicketTrigger.Fixed,
            new PatchInput
            {
                By = TicketBoard.Name(run.UserKey),
                Note = $"fix: {patch}",
                FreeFrom = check.FreeFrom,
                Inclusive = check.Inclusive,
            },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept($"{patch}: the repro case and {cases.Count - 1} earlier ones pass")
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }
}
