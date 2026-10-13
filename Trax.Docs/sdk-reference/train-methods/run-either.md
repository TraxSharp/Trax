---
title: RunEither
description: "Reference for RunEither, which executes a train from outside and returns its failure as Left instead of throwing."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Train
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 9
---

# RunEither

Executes the train from outside it and returns the result as an `Either<Exception, TReturn>`: `Right` on success, `Left` holding the exception on failure. [Run](/docs/sdk-reference/train-methods/run) throws instead.

## Signature

```csharp fragment
// Train<TInput, TReturn>, inherited unchanged by ServiceTrain<TIn, TOut>
public Task<Either<Exception, TReturn>> RunEither(TInput input);
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `TInput` | The train's input |

## Returns

`Task<Either<Exception, TReturn>>`. Nothing escapes it: a junction's exception, an exception `Junctions()` throws, and a cancelled run (as `OperationCanceledException`) all come back as `Left`. See [Either and Unit](/docs/sdk-reference/trains-and-junctions/either).

## Behavior

- Seeds Memory with the input and runs `Junctions()`, as `Run` does, without unwrapping the result.
- Takes no `CancellationToken`. It runs with the train's current `CancellationToken`, which a caller cannot set; pass a token to `Run`, or run the train through a host (the mediator or the scheduler), which sets it.
- On a `ServiceTrain` it runs the chain only: no metadata row is created, no lifecycle hook fires and no outcome is written, and an `EffectJunction` in the chain fails with a `TrainException` because the run has no metadata. `Run` does those.
- A cancelled run's `Left` is an `OperationCanceledException`; test for it before treating the `Left` as a business failure.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class OrderPreview
{
    public async Task<string> Process(OrderInput input)
    {
        var result = await new LocalOrderTrain().RunEither(input);

        return result.Match(
            Right: success => $"Order processed: {success.ConfirmationId}",
            Left: error => error is OperationCanceledException ? "Cancelled" : $"Failed: {error.Message}"
        );
    }
}

public class LocalOrderTrain : Train<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Extract<OrderInput, OrderDetails>().Chain<ProcessOrder>().Resolve();
}
```

## See also

- [Run](/docs/sdk-reference/train-methods/run)
- [Either and Unit](/docs/sdk-reference/trains-and-junctions/either)
