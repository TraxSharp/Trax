using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Hands a refund the model was unsure about to a person. The page can time this step out, as it can
/// the other two tracks, so a crash armed for the refund fires whichever track the model picks.
/// </summary>
public class QueueForReview(FaultInjector faults, DemoPace pace)
    : EffectJunction<RefundCase, RefundOutcome>
{
    public override async Task<RefundOutcome> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);

        if (faults.TryFire(refund.RunId, CrashPoint.RefundTrack))
        {
            await Task.Delay(pace.StepDelay);
            throw new TimeoutException(
                "The review queue did not accept the case in time (timeout injected by the demo)."
            );
        }

        return new RefundOutcome(refund.RunId, refund.OrderId, "QueuedForReview", 0m);
    }
}
