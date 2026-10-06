---
layout: default
title: Run / RunEither
description: Reference for Run and RunEither, which execute a train from outside, throwing on failure or returning an Either, and how ServiceTrain seals them.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 8
---

# Run / RunEither

Executes the train from the outside. `Run` throws on failure; `RunEither` returns an `Either<Exception, TReturn>` for Railway-oriented error handling.

These are called by **consumers** of the train, not inside `Junctions()`.

## Signatures

### Run (throws on failure)

```csharp
public virtual async Task<TReturn> Run(TInput input, CancellationToken cancellationToken = default)
```

On a `ServiceTrain` it is a sealed override, and the two overloads that take a pre-created
`Metadata` are not virtual:

```csharp
public sealed override Task<TOut> Run(TIn input, CancellationToken cancellationToken = default)
public Task<TOut> Run(TIn input, Metadata metadata)
public Task<TOut> Run(TIn input, Metadata metadata, CancellationToken cancellationToken)
```

A service train does its work in `Junctions()`, and `Run` owns the metadata row, the lifecycle
hooks and the outcome write around it, so none of them can be overridden. A plain
`Train<TInput, TReturn>` keeps a virtual `Run`.

### RunEither (returns Either)

```csharp
public Task<Either<Exception, TReturn>> RunEither(TInput input)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `TInput` | Yes | The input data for the train |
| `cancellationToken` | `CancellationToken` | No | Token to monitor for cancellation requests. When provided, it is stored on the `Train.CancellationToken` property and propagated to every junction before execution. Defaults to `CancellationToken.None` when omitted. |

> **Note:** `RunEither` does not accept a `CancellationToken` parameter, and a caller cannot assign `Train.CancellationToken` (its setter is not public). Pass the token to `Run`, or run the train through a host (a `ServiceTrain` resolved from the container, the mediator or the scheduler), which sets it.

## Returns

- **`Run`**: `Task<TReturn>`, the train result. Throws the captured exception if the train failed. Throws `OperationCanceledException` if the token is cancelled.
- **`RunEither`**: `Task<Either<Exception, TReturn>>`, `Left` on failure, `Right` on success. Nothing escapes it, cancellation included: a cancelled run returns `Left` holding the `OperationCanceledException`. Tell it apart from a business failure with `is OperationCanceledException` before treating the `Left` as one.

## Examples

### Using Run (imperative style)

```csharp
try
{
    var result = await train.Run(new OrderInput { OrderId = "123" });
    Console.WriteLine($"Order processed: {result.ConfirmationId}");
}
catch (Exception ex)
{
    Console.WriteLine($"Train failed: {ex.Message}");
}
```

### Using RunEither (functional style)

```csharp
var result = await train.RunEither(new OrderInput { OrderId = "123" });

result.Match(
    Right: success => Console.WriteLine($"Order processed: {success.ConfirmationId}"),
    Left: error => Console.WriteLine($"Train failed: {error.Message}")
);
```

### With CancellationToken

```csharp
// From an ASP.NET controller
public async Task<IActionResult> ProcessOrder(
    OrderInput input,
    CancellationToken cancellationToken)
{
    var result = await train.Run(input, cancellationToken);
    return Ok(result);
}
```

## Behavior

1. If a `CancellationToken` is provided, stores it on the `Train.CancellationToken` property.
2. Seeds `Memory` with the input.
3. Calls the train's `Junctions()` declaration.
4. **`Run`**: Unwraps the `Either` result. If `Left`, rethrows the exception. If `Right`, returns the value.
5. **`RunEither`**: Returns the `Either` directly without unwrapping.

During junction execution, the train's `CancellationToken` is automatically propagated to each junction before its `Run` method is called. Junctions access the token via `this.CancellationToken`. Before each junction executes, `CancellationToken.ThrowIfCancellationRequested()` is called. If the token is already cancelled, the junction is skipped entirely.

## Remarks

- `RunEither` is useful when you want functional-style error handling without try/catch. Read it with `Either`'s `Match`, `IfRight` and `IfLeft`, or check `IsRight` and cast.
- In most applications, trains are executed through `ITrainBus.RunAsync` (which calls `Run` internally) rather than calling `Run` directly. See [TrainBus](/docs/sdk-reference/mediator-api/train-bus).
- The `cancellationToken` parameter stores the token before calling the train's route definition. All junctions in the chain then receive the token automatically. See [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens) for details on how cancellation propagates through the pipeline.
- `Run` is `virtual` on plain `Train<TInput, TReturn>`, for code that uses Trax.Core without Trax.Effect. An override that does not call `base.Run` never reaches `Junctions()`, so the chain that `DeclaredChain()` reads, and any check built on it, is not the chain that runs. Call `base.Run` from an override, or put the work in a junction. A `ServiceTrain` cannot override `Run` at all: its overloads are sealed. `Trax.Core/docs/adr/0003` records why.
