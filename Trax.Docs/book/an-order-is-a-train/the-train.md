---
title: "1.6 The train"
description: "Chapter 1 of the Trax book, section 6: The train."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/an-order-is-a-train/the-junctions
---

# 1.6 The train

`Trains/PlaceOrder/IPlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/IPlaceOrderTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public interface IPlaceOrderTrain : IServiceTrain<PlaceOrderInput, PlaceOrderOutput>;
```

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<PriceCartJunction>()
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

The train is only the order of its junctions. Nothing here passes a value from one junction to the
next: the train carries a store called Memory, which holds the input when the train starts and
each junction's output as it finishes, one value per type. Every junction takes its input from
there. That is how `TakePaymentJunction` gets the `PlaceOrderInput` even though the junction just
before it made a `StockReservation`.

Nothing reads the `StockReservation` yet. In chapter 3 the payment starts failing, and
`TakePaymentJunction` takes the reservation too, to give the stock back before the order stops.

`IPlaceOrderTrain` is how the rest of the application asks for the train, and its full name is the
name Trax records each run under.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.7 Run it](/docs/book/an-order-is-a-train/run-it)
