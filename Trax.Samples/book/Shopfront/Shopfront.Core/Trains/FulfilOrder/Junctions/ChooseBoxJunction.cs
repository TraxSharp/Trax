using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class ChooseBoxJunction(IWarehouse warehouse, ILogger<ChooseBoxJunction> logger)
    : EffectJunction<FulfilOrderInput, Box>
{
    public override async Task<Box> Run(FulfilOrderInput input)
    {
        logger.LogInformation("Choosing a box for order {OrderId}", input.OrderId);
        var box = await warehouse.ChooseBox(input.Items, CancellationToken);
        logger.LogInformation("Chose a {Size} box for order {OrderId}", box.Size, input.OrderId);
        return box;
    }
}
