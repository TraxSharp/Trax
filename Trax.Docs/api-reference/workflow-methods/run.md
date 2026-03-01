---
layout: default
title: Run / RunEither
parent: Workflow Methods
grand_parent: API Reference
nav_order: 8
---

# Run / RunEither

Executes the workflow from the outside. `Run` throws on failure; `RunEither` returns an `Either<Exception, TReturn>` for Railway-oriented error handling.

These are called by **consumers** of the workflow, not inside `RunInternal`.

## Signatures

### Run (throws on failure)

```csharp
public async Task<TReturn> Run(TInput input)
public async Task<TReturn> Run(TInput input, IServiceProvider serviceProvider)
public async Task<TReturn> Run(TInput input, CancellationToken cancellationToken)
public async Task<TReturn> Run(TInput input, IServiceProvider serviceProvider, CancellationToken cancellationToken)
```

### RunEither (returns Either)

```csharp
public Task<Either<Exception, TReturn>> RunEither(TInput input)
public Task<Either<Exception, TReturn>> RunEither(TInput input, IServiceProvider serviceProvider)
public Task<Either<Exception, TReturn>> RunEither(TInput input, CancellationToken cancellationToken)
public Task<Either<Exception, TReturn>> RunEither(TInput input, IServiceProvider serviceProvider, CancellationToken cancellationToken)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `TInput` | Yes | The input data for the workflow |
| `serviceProvider` | `IServiceProvider` | No | A service provider for DI within the workflow. When provided, it's stored in Memory under `typeof(IServiceProvider)`. |
| `cancellationToken` | `CancellationToken` | No | Token to monitor for cancellation requests. When provided, it is stored on the `Workflow.CancellationToken` property and propagated to every step before execution. Defaults to `CancellationToken.None` when omitted. |

## Returns

- **`Run`**: `Task<TReturn>` — the workflow result. Throws the captured exception if the workflow failed. Throws `OperationCanceledException` if the token is cancelled.
- **`RunEither`**: `Task<Either<Exception, TReturn>>` — `Left` on failure, `Right` on success. Note: cancellation still throws `OperationCanceledException` rather than returning `Left` — cancellation is not a business error.

## Examples

### Using Run (imperative style)

```csharp
try
{
    var result = await workflow.Run(new OrderInput { OrderId = "123" });
    Console.WriteLine($"Order processed: {result.ConfirmationId}");
}
catch (Exception ex)
{
    Console.WriteLine($"Workflow failed: {ex.Message}");
}
```

### Using RunEither (functional style)

```csharp
var result = await workflow.RunEither(new OrderInput { OrderId = "123" });

result.Match(
    Right: success => Console.WriteLine($"Order processed: {success.ConfirmationId}"),
    Left: error => Console.WriteLine($"Workflow failed: {error.Message}")
);
```

### With ServiceProvider

```csharp
var result = await workflow.Run(input, serviceProvider);
// Steps can now resolve IServiceProvider from Memory
```

### With CancellationToken

```csharp
// From an ASP.NET controller
public async Task<IActionResult> ProcessOrder(
    OrderInput input,
    CancellationToken cancellationToken)
{
    var result = await workflow.Run(input, cancellationToken);
    return Ok(result);
}

// With both ServiceProvider and CancellationToken
var result = await workflow.Run(input, serviceProvider, cancellationToken);
```

## Behavior

1. If a `CancellationToken` is provided, stores it on the `Workflow.CancellationToken` property.
2. Initializes `Memory` with `Unit.Default` (and optionally `IServiceProvider`).
3. Calls `RunInternal(input)` — the user-implemented method.
4. **`Run`**: Unwraps the `Either` result. If `Left`, rethrows the exception. If `Right`, returns the value.
5. **`RunEither`**: Returns the `Either` directly without unwrapping.

During step execution, the workflow's `CancellationToken` is automatically propagated to each step before its `Run` method is called. Steps access the token via `this.CancellationToken`. Before each step executes, `CancellationToken.ThrowIfCancellationRequested()` is called — if the token is already cancelled, the step is skipped entirely.

## Remarks

- `RunEither` is useful when you want functional-style error handling without try/catch. It pairs naturally with LanguageExt's `Match`, `Map`, `Bind`, etc.
- In most applications, workflows are executed through `IWorkflowBus.RunAsync` (which calls `Run` internally) rather than calling `Run` directly. See [WorkflowBus]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %}).
- The `serviceProvider` overload enables steps to resolve additional services beyond what was placed in Memory via `AddServices`.
- The `cancellationToken` overloads store the token before calling `RunInternal`. All steps in the chain then receive the token automatically. See [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %}) for details on how cancellation propagates through the pipeline.
