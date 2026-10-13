using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>A courier collects the order from the restaurant.</summary>
public class PickUp(DemoPace pace) : EffectJunction<Cooked, PickedUp>
{
    public override async Task<PickedUp> Run(Cooked order)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);
        return new PickedUp(order.OrderId, "Sam");
    }
}
