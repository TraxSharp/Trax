using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>The model said the customer no longer wants the order: check it is still within the return window.</summary>
public class CheckReturnWindow(DemoPace pace) : EffectJunction<RefundCase, RefundEvidence>
{
    public override async Task<RefundEvidence> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);
        return new RefundEvidence(
            refund.RunId,
            refund.OrderId,
            refund.Amount,
            refund.PriorRefunds,
            nameof(Problem.Unwanted),
            "The order is inside the 30-day return window."
        );
    }
}
