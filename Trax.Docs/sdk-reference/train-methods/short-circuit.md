---
title: ShortCircuit
description: "Reference for ShortCircuit, which runs a junction whose successful result becomes the train's return value at Resolve, while its failure is ignored."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.ShortCircuit.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 3
---

# ShortCircuit

Runs a junction whose output is the train's return type. When the junction succeeds, its value is kept as the short-circuit result, and [Resolve](/docs/sdk-reference/train-methods/resolve) returns it instead of looking in Memory. When it fails, the failure is ignored and the train carries on.

## Signatures

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public
protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>()
    where TJunction : class;

protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>(TJunction instance)
    where TJunction : class;
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `TJunction` | The junction type. It implements `IJunction<TIn, TReturn>` for some `TIn`. |
| `instance` | A junction already built, which the step runs as it is |

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

1. Skipped, like every step, when an earlier step has failed.
2. Builds the junction as [Chain](/docs/sdk-reference/train-methods/chain) does and takes its input from Memory. A missing input or constructor argument fails the train.
3. Runs the junction.
4. On success, stores the output in Memory under its type and keeps it as the short-circuit value. On failure, keeps nothing and sets no exception.

The steps after a `ShortCircuit` still run, whether or not it succeeded: the short-circuit value changes only what `Resolve()` returns. A later step that fails puts the train on the failure track, and `Resolve()` then returns that failure rather than the short-circuit value.

This test, from Trax.Core's suite, puts a cache in front of a price lookup. On a hit the cached price is the result and the lookup still runs; on a miss the cache's exception is dropped:

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Caching/CachedPriceTests.cs
public class PriceTrain(IPriceCache cache, IAudit audit) : Train<Product, Price>
{
    protected override Task<Either<Exception, Price>> Junctions() =>
        AddServices(cache, audit).ShortCircuit<CheckCache>().Chain<LookUpPrice>().Resolve();
}
// ...
    [Test]
    public async Task Run_CacheHit_ReturnsTheCachedPriceButStillRunsTheLookUp()
    {
        var cache = new PriceCache();
        cache.Prices["tea"] = 4m;
        var audit = new Audit();

        var price = await new PriceTrain(cache, audit).Run(new Product("tea"));

        price.Should().Be(new Price(4m));
        audit.Lines.Should().Equal("looked up tea");
    }

    [Test]
    public async Task Run_CacheMiss_IgnoresTheFailureAndReturnsTheLookUp()
    {
        var price = await new PriceTrain(new PriceCache(), new Audit()).Run(new Product("tea"));

        price.Should().Be(new Price(10m));
    }
```

| Condition | Result |
|-----------|--------|
| The junction's output cannot be `TReturn` | Refused by the startup chain check |
| Used inside a `Parallel` branch | Refused by the startup chain check |

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class CachedOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        ShortCircuit<CheckCache>()          // a cached result, when there is one
            .Chain<ValidateOrder>()         // still runs on a cache hit
            .Chain<ProcessPayment>()
            .Resolve();                     // the cached result, or ProcessPayment's
}
```

## See also

- [Chain](/docs/sdk-reference/train-methods/chain)
- [Resolve](/docs/sdk-reference/train-methods/resolve), and the order it resolves in
