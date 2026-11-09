---
title: "14.5 The check"
description: "Chapter 14 of the Trax book, section 5: The check."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/returns-are-written-as-features/derive-the-machine
---

# 14.5 The check

`Checking` runs `ReturnOrder`. It takes the order id and the return's owner, and answers whether
the return is accepted.

`Shopfront.Core/Trains/ReturnOrder/ReturnOrderInput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/ReturnOrder/ReturnOrderInput.cs
namespace Shopfront.Trains.ReturnOrder;

// What a return's Checking state hands the train: the order from the return's context, and the
// return's owner, whom the server names.
public record ReturnOrderInput
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
}
```

`Shopfront.Core/Trains/ReturnOrder/ReturnOrderOutput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/ReturnOrder/ReturnOrderOutput.cs
namespace Shopfront.Trains.ReturnOrder;

public record ReturnOrderOutput(bool Accepted);
```

`Shopfront.Core/Trains/ReturnOrder/IReturnOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/ReturnOrder/IReturnOrderTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.ReturnOrder;

public interface IReturnOrderTrain : IServiceTrain<ReturnOrderInput, ReturnOrderOutput>;
```

`Shopfront.Core/Trains/ReturnOrder/ReturnOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/ReturnOrder/ReturnOrderTrain.cs
using Shopfront.Auth;
using Shopfront.Trains.ReturnOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.ReturnOrder;

// Run by a return's Checking state, for the customer who owns the return.
[TraxAuthorize(Roles = Roles.Customer)]
public class ReturnOrderTrain : ServiceTrain<ReturnOrderInput, ReturnOrderOutput>, IReturnOrderTrain
{
    protected override Task<Either<Exception, ReturnOrderOutput>> Junctions() =>
        Chain<CheckTheOrderJunction>().Resolve();
}
```

`Shopfront.Core/Trains/ReturnOrder/Junctions/CheckTheOrderJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/ReturnOrder/Junctions/CheckTheOrderJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.ReturnOrder.Junctions;

// Accepts a return only of an order the return's owner placed. An id that names no order of
// theirs, or is not an id at all, is refused rather than failed: the customer can fix it.
internal class CheckTheOrderJunction(IShopfrontDbContext db)
    : EffectJunction<ReturnOrderInput, ReturnOrderOutput>
{
    public override async Task<ReturnOrderOutput> Run(ReturnOrderInput input)
    {
        if (!Guid.TryParse(input.OrderId, out var orderId))
            return new ReturnOrderOutput(Accepted: false);

        var theirs = await db.Orders.AnyAsync(
            o => o.Id == orderId && o.CustomerId == input.CustomerId,
            CancellationToken
        );
        return new ReturnOrderOutput(theirs);
    }
}
```

- **The names are the glossary's.** The run's input is sent as JSON, so `orderId` and
  `customerId` in the glossary's `input` are `OrderId` and `CustomerId` here, and `accepted` in
  its `output` is `Accepted`.
- **`CustomerId` is the owner.** It is `$owner` in the glossary, set by the server from the
  return's owner, the same qualified id, `TraxJwt:ada`, an order's `CustomerId` holds. The shop's
  query filter narrows `Orders` to the caller too, and on the worker the caller is the return's
  owner, as it is for a checkout's `PlaceOrder` (chapter 13).
- **A refusal is an answer, not a failure.** An order that is not hers is `Accepted: false`, which
  the feature calls `the return is refused`. A run that throws is `the return cannot be checked`.
- **`[TraxAuthorize]`** names the role the return's owner has, as `PlaceOrder` does for checkout.

The worker already scans the library for trains, so it runs `ReturnOrder` with no change.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.6 Serve it](/docs/book/returns-are-written-as-features/serve-it)
