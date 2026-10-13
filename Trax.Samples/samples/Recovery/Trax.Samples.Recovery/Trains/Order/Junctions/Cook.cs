using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>The kitchen makes the order.</summary>
public class Cook(DemoPace pace) : EffectJunction<SentToRestaurant, Cooked>
{
    public override async Task<Cooked> Run(SentToRestaurant order)
    {
        await Task.Delay(pace.StepDelay * 2, CancellationToken);
        return new Cooked(order.OrderId);
    }
}
