---
title: "1.4 What an order carries"
description: "Chapter 1 of the Trax book, section 4: What an order carries."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/an-order-is-a-train/the-shop
---

# 1.4 What an order carries

A train takes one input and returns one output, and each junction in it does the same. Every value
that moves between junctions gets a type of its own.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderInput.cs
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required string CustomerId { get; init; }
    public required List<OrderLine> Lines { get; init; }
}
```

`Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderOutput.cs
namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(Guid OrderId, decimal Total, string TransactionId);
```

`PlaceOrderInput` is what the customer sends and `PlaceOrderOutput` is what they get back.
Every other type goes in `Models/`. An order line is part of the input, so callers build it, and it
is `public`:

`Trains/PlaceOrder/Models/OrderLine.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/OrderLine.cs
namespace Shopfront.Trains.PlaceOrder.Models;

public record OrderLine(string Sku, int Quantity);
```

The other four are what the junctions hand each other on the way. Nothing outside the train should
depend on them, so they are `internal`, as the junctions below are:

`Trains/PlaceOrder/Models/PricedCart.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/PricedCart.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record PricedCart(List<PricedLine> Lines, decimal Total);
```

`Trains/PlaceOrder/Models/PricedLine.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/PricedLine.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record PricedLine(string Sku, int Quantity, decimal UnitPrice);
```

`Trains/PlaceOrder/Models/StockReservation.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/StockReservation.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record StockReservation(Guid Id);
```

`Trains/PlaceOrder/Models/Payment.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/Payment.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record Payment(string TransactionId, decimal Amount);
```

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.5 The junctions](/docs/book/an-order-is-a-train/the-junctions)
