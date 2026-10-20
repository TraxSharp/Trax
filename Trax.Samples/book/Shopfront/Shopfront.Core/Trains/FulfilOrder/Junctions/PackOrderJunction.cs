using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class PackOrderJunction(IWarehouse warehouse, ILogger<PackOrderJunction> logger)
    : EffectJunction<(FulfilOrderInput Order, ShippingLabel Label), FulfilOrderOutput>
{
    public override async Task<FulfilOrderOutput> Run(
        (FulfilOrderInput Order, ShippingLabel Label) input
    )
    {
        logger.LogInformation("Packing order {OrderId}", input.Order.OrderId);
        await warehouse.Pack(
            input.Order.OrderId,
            input.Order.Items,
            input.Label.TrackingNumber,
            CancellationToken
        );
        logger.LogInformation(
            "Packed order {OrderId} as {TrackingNumber}",
            input.Order.OrderId,
            input.Label.TrackingNumber
        );
        return new FulfilOrderOutput(input.Order.OrderId, input.Label.TrackingNumber);
    }
}
