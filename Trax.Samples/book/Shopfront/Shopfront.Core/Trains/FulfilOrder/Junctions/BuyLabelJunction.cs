using Shopfront.Couriers;
using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class BuyLabelJunction(ICourier courier, ILogger<BuyLabelJunction> logger)
    : EffectJunction<(FulfilOrderInput Order, Box Box, CourierRates Rates), ShippingLabel>
{
    public override async Task<ShippingLabel> Run(
        (FulfilOrderInput Order, Box Box, CourierRates Rates) input
    )
    {
        var price = input.Rates.Base + input.Rates.PerKilogram * input.Box.Kilograms;
        var trackingNumber = await courier.BuyLabel(input.Order.OrderId, price, CancellationToken);
        logger.LogInformation("Bought label {TrackingNumber} for {Price}", trackingNumber, price);
        return new ShippingLabel(trackingNumber, price);
    }
}
