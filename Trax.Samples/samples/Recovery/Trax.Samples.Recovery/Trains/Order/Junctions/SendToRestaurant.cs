using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>
/// Sends the order to the restaurant's tablet. A tablet that does not answer in time is tried once more; a closed
/// restaurant fails the run here, before the card is charged.
/// </summary>
public class SendToRestaurant(Restaurant restaurant, FaultInjector faults, DemoPace pace)
    : EffectJunction<OrderInput, SentToRestaurant>
{
    public override async Task<SentToRestaurant> Run(OrderInput order)
    {
        faults.ClaimNextOrder(order.OrderId);
        try
        {
            await restaurant.SendAsync(order.OrderId, attempt: 1, CancellationToken);
        }
        catch (TimeoutException)
        {
            // Wait a moment before trying the tablet again.
            await Task.Delay(pace.TabletRetryBackoff, CancellationToken);
            await restaurant.SendAsync(order.OrderId, attempt: 2, CancellationToken);
        }
        return new SentToRestaurant(order.OrderId);
    }
}
