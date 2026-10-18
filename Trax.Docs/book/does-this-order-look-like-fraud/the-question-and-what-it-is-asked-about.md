---
title: "5.2 The question and what it is asked about"
description: "Chapter 5 of the Trax book, section 2: The question and what it is asked about."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/does-this-order-look-like-fraud/a-question-not-an-if
---

# 5.2 The question and what it is asked about

The question is a type, so the chain can name it and Trax can key its answers by it. Its words go
on the type, once: a model reads them, and so does anyone reading the record later.

By the end of the chapter the new files are:

```text
Shopfront/
├── Extensions/
│   └── ShopfrontServiceCollectionExtensions.cs
├── Fraud/
│   ├── LooksFraudulent.cs
│   ├── OrderRisk.cs
│   ├── FraudRules.cs
│   ├── ICandidateFraudDecider.cs
│   └── StricterFraudRules.cs
└── Trains/PlaceOrder/Junctions/
    ├── AssessRiskJunction.cs
    └── RefuseOrderJunction.cs
```

Fraud is the shop's policy rather than one train's, so it gets a folder outside `Trains/`, like the
catalogue.

`Fraud/LooksFraudulent.cs`:

```csharp compile=app file=Fraud/LooksFraudulent.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

[Asks(
    "Does this order look like a stolen card being used?",
    Yes = "Hold it: it matches how stolen cards are used.",
    No = "Take it: nothing about it is unusual."
)]
public sealed class LooksFraudulent;
```

A decider is asked about one value in Memory. The cart and the input together hold too much, and
not the one thing that matters most: whether this customer has ordered before. A value of its own
says exactly what the question is about.

`Fraud/OrderRisk.cs`:

```csharp compile=app file=Fraud/OrderRisk.cs
namespace Shopfront.Fraud;

public record OrderRisk(string CustomerId, decimal Total, bool FirstOrder);
```

A junction gathers it.

`Trains/PlaceOrder/Junctions/AssessRiskJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/AssessRiskJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class AssessRiskJunction(IShopfrontDbContext db)
    : EffectJunction<(PlaceOrderInput Order, PricedCart Cart), OrderRisk>
{
    public override async Task<OrderRisk> Run((PlaceOrderInput Order, PricedCart Cart) input)
    {
        var ordered = await db.Orders.AnyAsync(
            o => o.CustomerId == input.Order.CustomerId,
            CancellationToken
        );
        return new OrderRisk(input.Order.CustomerId, input.Cart.Total, FirstOrder: !ordered);
    }
}
```

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.3 The tracks](/docs/book/does-this-order-look-like-fraud/the-tracks)
