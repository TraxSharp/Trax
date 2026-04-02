---
layout: default
title: TrainBus
parent: Mediator API
grand_parent: SDK Reference
nav_order: 1
---

# TrainBus

The `ITrainBus` interface provides dynamic train dispatch by input type. Instead of injecting specific train interfaces, inject `ITrainBus` and call `RunAsync` with the input. The bus discovers and executes the correct train automatically.

## Methods

### RunAsync\<TOut\>

Executes the train registered for the input's type and returns a typed result.

```csharp
Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null)
Task<TOut> RunAsync<TOut>(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainInput` | `object` | Yes | N/A | The input object. Its runtime type is used to discover the registered train. |
| `cancellationToken` | `CancellationToken` | No | N/A | Token to monitor for cancellation requests. Forwarded to the train's `Run` method and propagated to all steps. |
| `metadata` | `Metadata?` | No | `null` | Optional parent metadata. When provided, establishes a parent-child relationship: the new train's `Metadata.ParentId` is set to this metadata's ID. |

**Returns**: `Task<TOut>`, the train's output.

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
// Inside a train junction
public class ProcessOrderJunction(ITrainBus trainBus) : EffectJunction<OrderInput, OrderResult>
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

## Scope Isolation

Each `RunAsync` call creates a child DI scope. The train and all its dependencies are resolved from this scope, which is disposed when the call returns. This means:

- **Blazor Server safe**: circuit-scoped services don't leak between train executions
- **Resource cleanup**: scoped services (`DbContext`, etc.) are disposed after each train
- **Nested isolation**: when a train dispatches another train via `ITrainBus`, the child train gets its own scope. Each train is a black box
- **Scheduler compatible**: the scheduler already creates per-job scopes; the additional child scope from `TrainBus` adds isolation for the actual train within the job runner's scope

`InitializeTrain` does **not** create a child scope. It resolves from the `TrainBus`'s own scope. This is an internal method used by the scheduler infrastructure.

## Remarks

- Trains are discovered by input type at registration time (via [AddMediator](/docs/sdk-reference/configuration/add-service-train-bus)). Each input type maps to exactly one train.
- The `metadata` parameter enables parent-child train chains, which is useful for tracking nested train executions in the dashboard.
- `RunAsync` calls the train's `Run` method internally, which means exceptions are thrown (not returned as `Either`). Use try/catch for error handling.
- The `cancellationToken` overloads forward the token to `train.Run(input, cancellationToken)`, which propagates it to all steps. See [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens) for details.
