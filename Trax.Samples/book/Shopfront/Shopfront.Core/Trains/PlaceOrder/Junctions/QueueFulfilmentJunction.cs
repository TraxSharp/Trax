using System.Text.Json;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainExecution;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class QueueFulfilmentJunction(
    ITrainExecutionService trains,
    ILogger<QueueFulfilmentJunction> logger
) : EffectJunction<(PlaceOrderOutput Order, PricedCart Cart), Unit>
{
    public override async Task<Unit> Run((PlaceOrderOutput Order, PricedCart Cart) input)
    {
        var queued = await trains.QueueAsync(
            typeof(IFulfilOrderTrain).FullName!,
            JsonSerializer.Serialize(
                new FulfilOrderInput
                {
                    OrderId = input.Order.OrderId,
                    Items = input.Cart.Lines.Sum(l => l.Quantity),
                }
            ),
            ct: CancellationToken
        );
        logger.LogInformation(
            "Queued fulfilment of {OrderId} as work queue entry {Entry}",
            input.Order.OrderId,
            queued.WorkQueueId
        );
        return Unit.Default;
    }
}
