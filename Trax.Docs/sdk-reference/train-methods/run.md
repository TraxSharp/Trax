---
title: Run
description: "Reference for Run, which executes a train from outside and throws its failure, on Train and the sealed overloads of ServiceTrain."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Train
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 7
---

# Run

Executes the train from outside it and returns its result, throwing the failure. It is called by the train's consumers, not inside `Junctions()`. [RunEither](/docs/sdk-reference/train-methods/run-either) returns the failure instead.

## Signatures

```csharp fragment
// Train<TInput, TReturn> (Trax.Core)
public virtual Task<TReturn> Run(TInput input, CancellationToken cancellationToken = default);

// ServiceTrain<TIn, TOut> (Trax.Effect, namespace Trax.Effect.Services.ServiceTrain)
public sealed override Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
public Task<TOut> Run(TIn input, Metadata metadata);
public Task<TOut> Run(TIn input, Metadata metadata, CancellationToken cancellationToken);
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `TInput` | The train's input |
| `cancellationToken` | `CancellationToken` | Stored on the train's `CancellationToken` and handed to every junction before it runs. Defaults to `CancellationToken.None`. |
| `metadata` | `Metadata` | `ServiceTrain` only: a `Pending` row the caller already created, which the run uses instead of creating one. The mediator and the scheduler pass it. |

## Returns

`Task<TReturn>`, the train's result.

## Behavior

1. Stores the token on the train's `CancellationToken`. The overload with `metadata` and no token keeps the train's current one.
2. Seeds Memory with the input and runs `Junctions()`.
3. Returns the `Right` value, or rethrows the `Left` exception with its original stack trace.

Before each junction runs, the token is checked; a cancelled token skips the junction and `Run` throws `OperationCanceledException`.

On a `ServiceTrain`, `Run` also creates (or takes over) the run's metadata row, fires the lifecycle hooks and writes the outcome; see [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train#run). The overloads that take `metadata` throw `TrainException` when the row is not `Pending`.

`Run` is virtual on `Train` and sealed on `ServiceTrain`. An override on a plain `Train` that does not call `base.Run` never reaches `Junctions()`, so what [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) reads is not what runs.

Trains in an application are usually run through [ITrainBus.RunAsync](/docs/sdk-reference/mediator-api/i-train-bus), which calls `Run`.

## Example

```csharp compile
public class OrderEndpoint(IOrderTrain train)
{
    public async Task<string> Process(OrderInput input, CancellationToken cancellationToken)
    {
        try
        {
            var result = await train.Run(input, cancellationToken);
            return $"Order processed: {result.ConfirmationId}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Train failed: {ex.Message}";
        }
    }
}
```

## See also

- [RunEither](/docs/sdk-reference/train-methods/run-either)
- [Cancellation tokens](/docs/cross-cutting/cancellation-tokens), how a token reaches each junction
- [Train](/docs/sdk-reference/trains-and-junctions/train) and [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
