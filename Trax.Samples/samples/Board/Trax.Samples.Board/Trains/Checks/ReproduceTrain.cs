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

public interface IReproduceTrain : IServiceTrain<ReproCheck, CheckResult>;

/// <summary>
/// Runs a reproducer's case against the shop's rules as they are today. The case reproduces the bug when the policy
/// says one charge and the rules charge another; the expected charge the agent sends must be the policy's.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Reproducer)]
public class ReproduceTrain : ServiceTrain<ReproCheck, CheckResult>, IReproduceTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunReproCase>().Resolve();
}

public class RunReproCase(InvokedRun run, TicketBoard board)
    : EffectJunction<ReproCheck, CheckResult>
{
    public override async Task<CheckResult> Run(ReproCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken);
        if (
            await board.RefuseUnlessHeld(ticket, run.UserKey, "reproduce", CancellationToken) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var report = ticket!.Report;
        var policy = report.Policy.Charge(check.Subtotal);
        var charged = report.Current.Charge(check.Subtotal);
        var failed = ticket.Context.FailedCases ?? 0;

        if (check.ExpectedShipping != policy)
            return CheckResult.Refuse(
                $"expected {Money(check.ExpectedShipping)} at {Money(check.Subtotal)}, but the policy says {Money(policy)}",
                failed
            );

        if (charged == policy)
        {
            failed++;
            var noted = await board.Fire(
                ticket.Id,
                TicketTrigger.NotReproduced,
                new CaseInput
                {
                    By = TicketBoard.Name(run.UserKey),
                    Note = $"{Money(check.Subtotal)} pays {Money(charged)}, as it should",
                    Subtotal = check.Subtotal,
                    Charged = charged,
                    FailedCases = failed,
                },
                CancellationToken
            );
            if (noted is not AdvanceOutcome.Advanced)
                return CheckResult.Refuse(TicketBoard.Why(noted));
            return CheckResult.Refuse(
                $"{Money(check.Subtotal)} pays {Money(charged)}, which matches the policy: not reproduced",
                failed
            );
        }

        var moved = await board.Fire(
            ticket.Id,
            TicketTrigger.Reproduced,
            new CaseInput
            {
                By = TicketBoard.Name(run.UserKey),
                Note = $"repro: {Money(check.Subtotal)} pays {Money(charged)}",
                Subtotal = check.Subtotal,
                Charged = charged,
                FailedCases = failed,
            },
            CancellationToken
        );
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept(
                $"{Money(check.Subtotal)} pays {Money(charged)}; the policy says {Money(policy)}: reproduced",
                failed
            )
            : CheckResult.Refuse(TicketBoard.Why(moved), failed);
    }
}
