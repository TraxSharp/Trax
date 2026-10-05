using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Records that the refund was declined. The page can time this step out, as it can the other two
/// tracks, so a crash armed for the refund fires whichever track the model picks.
/// </summary>
public class DeclineRefund(FaultInjector faults, DemoPace pace)
    : EffectJunction<RefundCase, RefundOutcome>
{
    public override async Task<RefundOutcome> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);

        if (faults.TryFire(refund.RunId, CrashPoint.RefundTrack))
        {
            await Task.Delay(pace.StepDelay);
            throw new TimeoutException(
                "The order system did not record the decline in time (timeout injected by the demo)."
            );
        }

        return new RefundOutcome(refund.RunId, refund.OrderId, "Declined", 0m);
    }
}
