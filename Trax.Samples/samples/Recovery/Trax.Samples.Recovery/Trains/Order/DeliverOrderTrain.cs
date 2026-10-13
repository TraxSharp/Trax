using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Trains.Order.Junctions;

namespace Trax.Samples.Recovery.Trains.Order;

/// <summary>
/// A food order, from the restaurant to the door: send it to the restaurant, charge the card, cook, pick up,
/// deliver. The restaurant's tablet can time out, and is tried once more; a closed restaurant fails the run
/// before the card is charged.
/// </summary>
/// <remarks>
/// <para>
/// The <c>order</c> machine's <c>Ordered</c> state invokes it for whoever places the order, so it asks only for a
/// signed-in caller, as <c>PlaceOrder</c> does: the host compares the two at startup. Unlike the refund train it is
/// not <c>[TraxBroadcast]</c>, because a user's own machine may not invoke a train whose every run is broadcast to
/// every subscriber; its steps reach a client through the operations view of <c>onJunctionEvent</c>, as the topic
/// map's do. Every step is an <c>EffectJunction</c>, so leaving the state could cancel the run on any host.
/// </para>
/// <para>
/// The scheduler never retries a run a machine invoked: it has no manifest. A failed run is the machine's
/// <c>Ordered.failed</c>, so the tablet's retry lives inside <see cref="SendToRestaurant"/>, where the machine
/// sees one run that recovered.
/// </para>
/// </remarks>
[TraxAuthorize]
public class DeliverOrderTrain : ServiceTrain<OrderInput, OrderDelivered>, IDeliverOrderTrain
{
    protected override Task<Either<Exception, OrderDelivered>> Junctions() =>
        Chain<SendToRestaurant>()
            .Chain<ChargeCard>()
            .Chain<Cook>()
            .Chain<PickUp>()
            .Chain<Deliver>()
            .Resolve();
}
