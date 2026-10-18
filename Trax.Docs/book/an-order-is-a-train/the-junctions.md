---
title: "1.5 The junctions"
description: "Chapter 1 of the Trax book, section 5: The junctions."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/an-order-is-a-train/what-an-order-carries
---

# 1.5 The junctions

Placing an order takes four steps, so we write four junctions. They are `internal`: the rest of the
application only ever talks to the train.

`Trains/PlaceOrder/Junctions/PriceCartJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/PriceCartJunction.cs
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class PriceCartJunction(Catalogue catalogue, ILogger<PriceCartJunction> logger)
    : EffectJunction<PlaceOrderInput, PricedCart>
{
    public override Task<PricedCart> Run(PlaceOrderInput input)
    {
        if (input.Lines.Count == 0)
            throw new TrainException("The cart is empty.");

        var lines = input
            .Lines.Select(line =>
            {
                if (line.Quantity < 1)
                    throw new TrainException($"{line.Sku}: the quantity must be at least 1.");
                var price =
                    catalogue.PriceOf(line.Sku)
                    ?? throw new TrainException($"{line.Sku} is not in the catalogue.");
                return new PricedLine(line.Sku, line.Quantity, price);
            })
            .ToList();

        var cart = new PricedCart(lines, lines.Sum(l => l.UnitPrice * l.Quantity));
        logger.LogInformation("Priced {Count} lines at {Total}", lines.Count, cart.Total);
        return Task.FromResult(cart);
    }
}
```

Each junction says what it takes and what it makes in its type: `PriceCartJunction` takes a
`PlaceOrderInput` and makes a `PricedCart`. A junction that throws stops the order there, and
nothing after it runs. It throws a `TrainException` when the message is meant for the customer:
Trax passes a `TrainException`'s message on to whoever ran the train, and once Shopfront serves
GraphQL it hides the message of any other exception, which may say more than a customer should
read.

`Trains/PlaceOrder/Junctions/ReserveStockJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ReserveStockJunction.cs
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ReserveStockJunction(Catalogue catalogue, ILogger<ReserveStockJunction> logger)
    : EffectJunction<PricedCart, StockReservation>
{
    public override Task<StockReservation> Run(PricedCart cart)
    {
        var shortSku = catalogue.TryReserve([.. cart.Lines.Select(l => (l.Sku, l.Quantity))]);
        if (shortSku is not null)
            throw new TrainException($"{shortSku} is out of stock.");

        var reservation = new StockReservation(Guid.NewGuid());
        logger.LogInformation("Reserved stock as {ReservationId}", reservation.Id);
        return Task.FromResult(reservation);
    }
}
```

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(IPaymentProvider payments, ILogger<TakePaymentJunction> logger)
    : EffectJunction<(PlaceOrderInput Order, PricedCart Cart), Payment>
{
    public override async Task<Payment> Run((PlaceOrderInput Order, PricedCart Cart) input)
    {
        var transactionId = await payments.Charge(
            input.Order.CustomerId,
            input.Cart.Total,
            CancellationToken
        );
        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            transactionId
        );
        return new Payment(transactionId, input.Cart.Total);
    }
}
```

`TakePaymentJunction` takes a tuple. It needs the customer, which is on the input, and the total,
which is on the priced cart, so it asks for both.

It also hands the payment provider its `CancellationToken`. Every junction has one, the token the
train was run with, and a junction passes it to everything it waits on. When the customer gives
up on the request, the charge is abandoned with it instead of running on for nobody.

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override Task<PlaceOrderOutput> Run((PricedCart Cart, Payment Payment) input)
    {
        var output = new PlaceOrderOutput(
            Guid.NewGuid(),
            input.Cart.Total,
            input.Payment.TransactionId
        );
        logger.LogInformation("Confirmed order {OrderId}", output.OrderId);
        return Task.FromResult(output);
    }
}
```

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.6 The train](/docs/book/an-order-is-a-train/the-train)
