---
layout: default
title: TrainBus
parent: Mediator API
grand_parent: SDK Reference
nav_order: 1
---

# TrainBus

The `ITrainBus` interface provides dynamic train dispatch by input type. Instead of injecting specific train interfaces, inject `ITrainBus` and call `RunAsync` with the input — the bus discovers and executes the correct train automatically.

## Methods

### RunAsync\<TOut\>

Executes the train registered for the input's type and returns a typed result.

```csharp
Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null)
Task<TOut> RunAsync<TOut>(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainInput` | `object` | Yes | — | The input object. Its runtime type is used to discover the registered train. |
| `cancellationToken` | `CancellationToken` | No | — | Token to monitor for cancellation requests. Forwarded to the train's `Run` method and propagated to all steps. |
| `metadata` | `Metadata?` | No | `null` | Optional parent metadata. When provided, establishes a parent-child relationship — the new train's `Metadata.ParentId` is set to this metadata's ID. |

**Returns**: `Task<TOut>` — the train's output.

**Throws**: `TrainException` if no train is registered for the input's type. `OperationCanceledException` if the token is cancelled.

### RunAsync (void)

Executes the train registered for the input's type without returning a result.

```csharp
Task RunAsync(object trainInput, Metadata? metadata = null)
Task RunAsync(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

Parameters are identical to `RunAsync<TOut>`.

### InitializeTrain

Resolves and initializes (but does **not** run) the train for the given input type.

```csharp
object InitializeTrain(object trainInput)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `trainInput` | `object` | Yes | The input object used to discover the train |

**Returns**: The initialized train instance (as `object`).

## Examples

### Basic Dispatch

```csharp
public class OrderService(ITrainBus trainBus)
{
    public async Task<OrderResult> ProcessOrder(OrderInput input)
    {
        return await trainBus.RunAsync<OrderResult>(input);
    }
}
```

### With CancellationToken

```csharp
public class OrderController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ProcessOrder(
        OrderInput input,
        CancellationToken cancellationToken)
    {
        // Token is forwarded to the train and all its steps
        var result = await trainBus.RunAsync<OrderResult>(input, cancellationToken);
        return Ok(result);
    }
}
```

### Nested Trains (Parent-Child Metadata)

```csharp
// Inside a train step
public class ProcessOrderStep(ITrainBus trainBus) : EffectStep<OrderInput, OrderResult>
{
    public override async Task<OrderResult> Run(OrderInput input)
    {
        // Pass both CancellationToken and parent metadata
        var paymentResult = await trainBus.RunAsync<PaymentResult>(
            new PaymentInput { Amount = input.Total },
            CancellationToken,
            metadata: Metadata);

        return new OrderResult { PaymentId = paymentResult.Id };
    }
}
```

## Remarks

- Trains are discovered by input type at registration time (via [AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-train-bus.md %})). Each input type maps to exactly one train.
- The `metadata` parameter enables parent-child train chains — useful for tracking nested train executions in the dashboard.
- `RunAsync` calls the train's `Run` method internally, which means exceptions are thrown (not returned as `Either`). Use try/catch for error handling.
- The `cancellationToken` overloads forward the token to `train.Run(input, cancellationToken)`, which propagates it to all steps. See [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %}) for details.
