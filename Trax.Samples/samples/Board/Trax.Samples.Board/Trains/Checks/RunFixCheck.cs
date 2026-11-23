using System.Globalization;
using Trax.Core.Extensions;
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

public class RunFixCheck(InvokedRun run, TicketBoard board) : EffectJunction<FixCheck, CheckResult>
{
    public override async Task<CheckResult> Run(FixCheck check)
    {
        var ticket = await board.Find(check.TicketId, CancellationToken).ConfigureAwait(false);
        if (
            await board
                .RefuseUnlessHeld(ticket, run.UserKey, "fix", CancellationToken)
                .ConfigureAwait(false) is
            { } refusal
        )
            return CheckResult.Refuse(refusal);

        var report = ticket.NotNull().Report;
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
            return await RejectFix(ticket, why).ConfigureAwait(false);
        }

        var patch = check.Inclusive
            ? $"free from {Money(check.FreeFrom)}"
            : $"free above {Money(check.FreeFrom)}";
        var moved = await board
            .Fire(
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
            )
            .ConfigureAwait(false);
        return moved is AdvanceOutcome.Advanced
            ? CheckResult.Accept(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{patch}: the repro case and {cases.Count - 1} cases from earlier fixes pass"
                )
            )
            : CheckResult.Refuse(TicketBoard.Why(moved));
    }

    // The patch breaks a case: the ticket goes back to ReadyToFix with the reason.
    private async Task<CheckResult> RejectFix(Ticket ticket, string why)
    {
        await board
            .Fire(
                ticket.Id,
                TicketTrigger.FixRejected,
                new NoteInput
                {
                    By = TicketBoard.Name(run.UserKey),
                    Note = $"patch refused: {why}",
                },
                CancellationToken
            )
            .ConfigureAwait(false);
        return CheckResult.Refuse(why);
    }
}
