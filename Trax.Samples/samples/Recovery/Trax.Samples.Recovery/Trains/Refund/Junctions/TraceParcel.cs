using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>The model said the order never arrived: ask the courier where the parcel went.</summary>
public class TraceParcel(DemoPace pace) : EffectJunction<RefundCase, RefundEvidence>
{
    public override async Task<RefundEvidence> Run(RefundCase refund)
    {
        await Task.Delay(pace.StepDelay);
        return new RefundEvidence(
            refund.RunId,
            refund.OrderId,
            refund.Amount,
            refund.PriorRefunds,
            nameof(Problem.Lost),
            "The courier has no record of delivering it."
        );
    }
}
