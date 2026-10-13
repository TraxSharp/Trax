using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>The model said the order arrived damaged: ask the customer for photos of it.</summary>
public class RequestPhotos(DemoPace pace) : EffectJunction<RefundCase, RefundEvidence>
{
    public override async Task<RefundEvidence> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);
        return new RefundEvidence(
            refund.RunId,
            refund.OrderId,
            refund.Amount,
            refund.PriorRefunds,
            nameof(Problem.Damaged),
            "The customer's photos show the damage."
        );
    }
}
