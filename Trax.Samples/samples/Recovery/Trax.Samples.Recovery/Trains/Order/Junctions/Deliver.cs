using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>
/// The courier brings the order to the door. The run is over, so anything the demo armed for it and that never
/// fired is removed.
/// </summary>
public class Deliver(FaultInjector faults, DemoPace pace) : EffectJunction<PickedUp, OrderDelivered>
{
    public override async Task<OrderDelivered> Run(PickedUp order)
    {
        await Task.Delay(pace.StepDelay * 2, CancellationToken);
        faults.Disarm(order.OrderId);
        return new OrderDelivered(order.OrderId, order.Courier);
    }
}
