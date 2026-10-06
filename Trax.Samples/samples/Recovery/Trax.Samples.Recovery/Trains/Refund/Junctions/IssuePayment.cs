using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Pays the refund. Like the other two tracks, this is a step the page can time out: it waits, then
/// gives up the way a call to a slow payment provider would.
/// </summary>
public class IssuePayment(FaultInjector faults, DemoPace pace)
    : EffectJunction<RefundCase, RefundOutcome>
{
    public override async Task<RefundOutcome> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);

        if (faults.TryFire(refund.RunId, CrashPoint.RefundTrack))
        {
            await Task.Delay(pace.StepDelay);
            throw new TimeoutException(
                "The payment provider did not answer in time (timeout injected by the demo)."
            );
        }

        return new RefundOutcome(refund.RunId, refund.OrderId, "Paid", refund.Amount);
    }
}
