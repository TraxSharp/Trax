---
title: Chain
description: "Reference for Chain, which runs a junction with its input from Memory and stores its output back: every overload, its railway behaviour and its failures."
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
nav_order: 1
---

# Chain

Runs a junction, taking its input from Memory and storing its output back in Memory. When an earlier step has failed, the junction is skipped and the failure passes on. Every overload is a protected method on `Train<TInput, TReturn>`, and a public method of the same shape on [MonadTask](/docs/sdk-reference/trains-and-junctions/monad-task) and `Monad`, so calls continue fluently. A chain always ends in [Resolve](/docs/sdk-reference/train-methods/resolve).

## Signatures

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public
protected MonadTask<TInput, TReturn> Chain<TJunction>()
    where TJunction : class;

protected MonadTask<TInput, TReturn> Chain<TJunction>(TJunction instance)
    where TJunction : class;

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
    where TJunction : IJunction<TIn, TOut>, new();

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
    where TJunction : IJunction<TIn, TOut>;

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
    where TJunction : IJunction<TIn, Unit>, new();

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
    where TJunction : IJunction<TIn, Unit>;
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `TJunction` | The junction type. For `Chain<TJunction>()`, it implements `IJunction<TIn, TOut>` for some `TIn` and `TOut`, found by reflection. |
| `TIn`, `TOut` | The explicit-type overloads state the input and output types instead of discovering them, for a junction that implements `IJunction<,>` more than once. The two-parameter form's output is `Unit`. |
| `instance`, `junction` | A junction already built, which the chain runs as it is |

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

1. If an earlier step has failed, the junction is skipped. Nothing is built for it: the junction, its constructor arguments and its input are not resolved.
2. `Chain<TJunction>()` builds the junction through its single public constructor, each parameter taken from Memory by type and then from the container. The parameterless explicit-type forms construct it with `new()`. An instance is used as it is.
3. The junction's input is taken from Memory by type. A tuple input is assembled from Memory alone.
4. The junction runs on the railway (see [Junction](/docs/sdk-reference/trains-and-junctions/junction#behavior)).
5. On success, the output is stored in Memory under its type; a tuple output is stored as its elements. On failure, the exception is kept and every later step is skipped.

Each junction is awaited with `ConfigureAwait(false)` and nothing blocks on it, so a chain runs under a single-threaded `SynchronizationContext` (Blazor Server, WPF, WinForms) without deadlocking.

| Condition | Result |
|-----------|--------|
| `TJunction` has more or fewer than one public constructor, is abstract, or is an interface | Refused when the chain is read, so the [startup check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) stops the host; outside a host the run fails with a `TrainException` naming the junction and its constructor count. An instance is not checked. |
| A constructor argument or the input is in neither Memory nor the container | The run fails with a `TrainException` naming the junction, the train and the type: `Junction 'ClockJunction' (train 'ScheduleTrain') needs 'IClock' as a constructor argument, but nothing earlier in the chain produced one and it is not registered in the container. Register it, pass one with AddServices, or chain a junction that outputs it first.` (an interface, on a train with a container; the advice differs for a class or a plain `Train`) On a host with the startup check, a missing constructor argument stops the host instead. |
| The junction's constructor throws | The run fails with that exception |

## Examples

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class PlaceOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>().Chain<ProcessPayment>().Resolve();
}
```

A junction built before the chain runs it:

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class PreviewOrderTrain : Train<OrderDetails, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain(new ProcessOrder()).Resolve();
}
```

## See also

- [IChain](/docs/sdk-reference/train-methods/i-chain), which resolves the junction by its interface
- [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit)
- [Memory](/docs/core/memory), how values are found by type, tuples included
