---
layout: default
title: Run / RunEither
parent: Train Methods
grand_parent: SDK Reference
nav_order: 8
---

# Run / RunEither

Executes the train from the outside. `Run` throws on failure; `RunEither` returns an `Either<Exception, TReturn>` for Railway-oriented error handling.

These are called by **consumers** of the train, not inside `RunInternal`.

## Signatures

### Run (throws on failure)

```csharp
public virtual async Task<TReturn> Run(TInput input, CancellationToken cancellationToken = default)
```

### RunEither (returns Either)

```csharp
public Task<Either<Exception, TReturn>> RunEither(TInput input)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `TInput` | Yes | The input data for the train |
| `cancellationToken` | `CancellationToken` | No | Token to monitor for cancellation requests. When provided, it is stored on the `Train.CancellationToken` property and propagated to every junction before execution. Defaults to `CancellationToken.None` when omitted. |

> **Note:** `RunEither` does not accept a `CancellationToken` parameter. Set the token via `Run` or by assigning `Train.CancellationToken` directly before calling `RunEither`.

## Returns

- **`Run`**: `Task<TReturn>` — the train result. Throws the captured exception if the train failed. Throws `OperationCanceledException` if the token is cancelled.
- **`RunEither`**: `Task<Either<Exception, TReturn>>` — `Left` on failure, `Right` on success. Note: cancellation still throws `OperationCanceledException` rather than returning `Left` — cancellation is not a business error.

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
2. Initializes `Memory` with `Unit.Default`.
3. Calls `RunInternal(input)` — the user-implemented method.
4. **`Run`**: Unwraps the `Either` result. If `Left`, rethrows the exception. If `Right`, returns the value.
5. **`RunEither`**: Returns the `Either` directly without unwrapping.

During junction execution, the train's `CancellationToken` is automatically propagated to each junction before its `Run` method is called. Junctions access the token via `this.CancellationToken`. Before each junction executes, `CancellationToken.ThrowIfCancellationRequested()` is called — if the token is already cancelled, the junction is skipped entirely.

## Remarks

- `RunEither` is useful when you want functional-style error handling without try/catch. It pairs naturally with LanguageExt's `Match`, `Map`, `Bind`, etc.
- In most applications, trains are executed through `ITrainBus.RunAsync` (which calls `Run` internally) rather than calling `Run` directly. See [TrainBus]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-bus.md %}).
- The `cancellationToken` parameter stores the token before calling `RunInternal`. All junctions in the chain then receive the token automatically. See [Cancellation Tokens]({{ site.baseurl }}{% link cross-cutting/cancellation-tokens.md %}) for details on how cancellation propagates through the pipeline.
