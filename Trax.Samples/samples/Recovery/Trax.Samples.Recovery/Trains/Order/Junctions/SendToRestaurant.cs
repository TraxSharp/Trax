using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>
/// Sends the order to the restaurant's tablet. A tablet that does not answer in time is tried once more; a closed
/// restaurant fails the run here, before the card is charged.
/// </summary>
public class SendToRestaurant(
    Restaurant restaurant,
    FaultInjector faults,
    DemoPace pace,
    OrderOwners owners
) : EffectJunction<OrderInput, SentToRestaurant>
{
    public override async Task<SentToRestaurant> Run(OrderInput order)
    {
        // The trouble a user armed is claimed by their own next order only.
        var owner = Metadata is { } step
            ? await owners.OwnerOfRun(step.TrainMetadataId, CancellationToken)
            : null;
        faults.ClaimNextOrder(owner, order.OrderId);
        try
        {
            await restaurant.SendAsync(order.OrderId, owner, attempt: 1, CancellationToken);
        }
        catch (TimeoutException)
        {
            // Wait a moment before trying the tablet again.
            await Task.Delay(pace.TabletRetryBackoff, CancellationToken);
            await restaurant.SendAsync(order.OrderId, owner, attempt: 2, CancellationToken);
        }
        return new SentToRestaurant(order.OrderId);
    }
}
