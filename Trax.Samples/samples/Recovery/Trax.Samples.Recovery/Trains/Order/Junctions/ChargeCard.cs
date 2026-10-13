using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Order.Junctions;

/// <summary>
/// Charges the card the order's total. The charge is keyed by the order's id, as a card processor's idempotency
/// key would be, so a run that executes twice charges once.
/// </summary>
public class ChargeCard(DemoPace pace) : EffectJunction<OrderInput, Charged>
{
    public override async Task<Charged> Run(OrderInput order)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);
        return new Charged(order.OrderId, $"ch_{order.OrderId}", order.Total);
    }
}
