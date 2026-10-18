---
title: "1.9 When the chain does not fit"
description: "Chapter 1 of the Trax book, section 9: When the chain does not fit."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/an-order-is-a-train/when-an-order-fails
---

# 1.9 When the chain does not fit

Swap the first two junctions in `PlaceOrderTrain`, so stock is reserved before the cart is priced:

```csharp fragment
Chain<ReserveStockJunction>()
    .Chain<PriceCartJunction>()
    .Chain<TakePaymentJunction>()
    .Chain<ConfirmOrderJunction>()
    .Resolve();
```

It builds. Run it, and the application refuses to start:

```text
Unhandled exception. Trax.Core.Exceptions.TrainException: 1 of 1 registered trains cannot run:
  - IPlaceOrderTrain: step 1 (ReserveStockJunction) needs 'Shopfront.Trains.PlaceOrder.Models.PricedCart' in Memory and nothing before it puts one there. Chain a junction that produces it first.
```

When the application starts, Trax reads every train's chain and checks that each junction's input
is in Memory by the time it runs. `ReserveStockJunction` needs a `PricedCart`, and at step 1 Memory
holds only the `PlaceOrderInput`. A chain that cannot run stops the application before it takes a
single request, rather than failing the first order that reaches it.

Swap the junctions back before going on.

## What we have

Shopfront takes an order, prices it, reserves stock, charges the customer and confirms it, as four
small junctions that each do one thing. A junction that fails stops the order where it is, and a
chain in the wrong order never starts.

What we cannot do yet is look back. The runs are recorded, but in memory, and they are gone when the
application stops. In chapter 2 we move them to Postgres, make the payment provider fail now and
then, and read back what happened to each order.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [2. Testing PlaceOrder](/docs/book/testing-place-order)
