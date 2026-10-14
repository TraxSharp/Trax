---
title: IChain
description: "Reference for IChain, which runs a junction resolved by its interface from Memory or the container, Inject properties included."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.Chain.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 2
---

# IChain

Runs a junction named by its interface. The junction is taken from Memory, or resolved from the container, rather than built by the chain, so a junction registered with [AddScopedTraxJunction](/docs/sdk-reference/trains-and-junctions/route-registration) arrives with its `[Inject]` properties filled. Otherwise it behaves as [Chain](/docs/sdk-reference/train-methods/chain).

## Signature

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public
protected MonadTask<TInput, TReturn> IChain<TJunction>()
    where TJunction : class;
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `TJunction` | An interface that extends `IJunction<TIn, TOut>` |

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

1. If an earlier step has failed, the step is skipped and nothing is resolved.
2. The junction is taken from Memory by `TJunction`, then from the container.
3. It runs as a `Chain` step does: input from Memory, output stored in Memory.

| Condition | Result |
|-----------|--------|
| `TJunction` is a class | Refused when the chain is read; at run time a `TrainException`: `Junction (<type>) must be an interface to call IChain.` |
| Neither Memory nor the container has a `TJunction` | The run fails with a `TrainException` naming the train and the interface |

The junction behind the interface is decided at run time, so the [declared graph](/docs/sdk-reference/train-methods/chain-graph) marks an `IChain` step `Opaque`, and the startup check does not check its constructor.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public interface IPriceOrder : IJunction<ValidatedOrder, OrderResult>;

public class PriceOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>().IChain<IPriceOrder>().Resolve();
}
```

The host registers the implementation with `services.AddScopedTraxJunction<IPriceOrder, PriceOrder>()`.

## See also

- [Chain](/docs/sdk-reference/train-methods/chain)
- [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration)
