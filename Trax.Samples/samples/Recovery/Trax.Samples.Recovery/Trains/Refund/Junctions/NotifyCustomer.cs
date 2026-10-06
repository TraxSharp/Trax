using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Tells the customer what happened, whichever track ran. The run is over, so anything still armed
/// for it is removed.
/// </summary>
public class NotifyCustomer(FaultInjector faults, DemoPace pace)
    : EffectJunction<RefundOutcome, RefundResult>
{
    public override async Task<RefundResult> Run(RefundOutcome outcome)
    {
        await Task.Delay(pace.StepDelay);
        faults.Disarm(outcome.RunId);
        return new RefundResult(
            outcome.OrderId,
            outcome.Status,
            outcome.Amount,
            CustomerNotified: true
        );
    }
}
