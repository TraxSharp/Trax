using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Tells the customer what happened, whichever track ran. It comes after the checkpoint, so the page
/// can crash it to show a retry that starts from the stored outcome. The run is over once it sends,
/// so anything still armed for it is removed.
/// </summary>
public class NotifyCustomer(FaultInjector faults, DemoPace pace)
    : EffectJunction<RefundOutcome, RefundResult>
{
    public override async Task<RefundResult> Run(RefundOutcome outcome)
    {
        await Task.Delay(pace.StepDelay);

        if (faults.TryFire(outcome.RunId, CrashPoint.RefundEmail))
        {
            await Task.Delay(pace.StepDelay);
            throw new TimeoutException(
                "The email service did not answer in time (timeout injected by the demo)."
            );
        }

        faults.Disarm(outcome.RunId);
        return new RefundResult(
            outcome.OrderId,
            outcome.Status,
            outcome.Amount,
            CustomerNotified: true
        );
    }
}
