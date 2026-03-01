---
layout: default
title: WorkflowBus
parent: Mediator API
grand_parent: API Reference
nav_order: 1
---

# WorkflowBus

The `IWorkflowBus` interface provides dynamic workflow dispatch by input type. Instead of injecting specific workflow interfaces, inject `IWorkflowBus` and call `RunAsync` with the input — the bus discovers and executes the correct workflow automatically.

## Methods

### RunAsync\<TOut\>

Executes the workflow registered for the input's type and returns a typed result.

```csharp
Task<TOut> RunAsync<TOut>(object workflowInput, Metadata? metadata = null)
Task<TOut> RunAsync<TOut>(object workflowInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `workflowInput` | `object` | Yes | — | The input object. Its runtime type is used to discover the registered workflow. |
| `cancellationToken` | `CancellationToken` | No | — | Token to monitor for cancellation requests. Forwarded to the workflow's `Run` method and propagated to all steps. |
| `metadata` | `Metadata?` | No | `null` | Optional parent metadata. When provided, establishes a parent-child relationship — the new workflow's `Metadata.ParentId` is set to this metadata's ID. |

**Returns**: `Task<TOut>` — the workflow's output.

**Throws**: `WorkflowException` if no workflow is registered for the input's type. `OperationCanceledException` if the token is cancelled.

### RunAsync (void)

Executes the workflow registered for the input's type without returning a result.

```csharp
Task RunAsync(object workflowInput, Metadata? metadata = null)
Task RunAsync(object workflowInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

Parameters are identical to `RunAsync<TOut>`.

### InitializeWorkflow

Resolves and initializes (but does **not** run) the workflow for the given input type.

```csharp
object InitializeWorkflow(object workflowInput)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `workflowInput` | `object` | Yes | The input object used to discover the workflow |

**Returns**: The initialized workflow instance (as `object`).

## Examples

### Basic Dispatch

```csharp
public class OrderService(IWorkflowBus workflowBus)
{
    public async Task<OrderResult> ProcessOrder(OrderInput input)
    {
        return await workflowBus.RunAsync<OrderResult>(input);
    }
}
```

### With CancellationToken

```csharp
public class OrderController(IWorkflowBus workflowBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ProcessOrder(
        OrderInput input,
        CancellationToken cancellationToken)
    {
        // Token is forwarded to the workflow and all its steps
        var result = await workflowBus.RunAsync<OrderResult>(input, cancellationToken);
        return Ok(result);
    }
}
```

### Nested Workflows (Parent-Child Metadata)

```csharp
// Inside a workflow step
public class ProcessOrderStep(IWorkflowBus workflowBus) : EffectStep<OrderInput, OrderResult>
{
    public override async Task<OrderResult> Run(OrderInput input)
    {
        // Pass both CancellationToken and parent metadata
        var paymentResult = await workflowBus.RunAsync<PaymentResult>(
            new PaymentInput { Amount = input.Total },
            CancellationToken,
            metadata: Metadata);

        return new OrderResult { PaymentId = paymentResult.Id };
    }
}
```

## Remarks

- Workflows are discovered by input type at registration time (via [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-workflow-bus.md %})). Each input type maps to exactly one workflow.
- The `metadata` parameter enables parent-child workflow chains — useful for tracking nested workflow executions in the dashboard.
- `RunAsync` calls the workflow's `Run` method internally, which means exceptions are thrown (not returned as `Either`). Use try/catch for error handling.
- The `cancellationToken` overloads forward the token to `workflow.Run(input, cancellationToken)`, which propagates it to all steps. See [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %}) for details.
