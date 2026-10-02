---
layout: default
title: TrainBus
description: "Reference for ITrainBus: RunAsync, RunByNameAsync and InitializeTrain, NoTrainForInputException, nested trains and scope isolation."
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
| `metadata` | `Metadata?` | No | `null` | A pre-created metadata record, in the `Pending` state, for the train to run as instead of creating its own. The scheduler and the dashboard's ad-hoc run use it. It is not a parent link: any other state, including a running train's metadata, is refused with a `TrainException`, and the run's `ParentId` is not set. |

**Returns**: `Task<TOut>`, the train's output.

**Throws**: [`NoTrainForInputException`](#notrainforinputexception) if no train is registered for the input's type (the message names the input type, the scanned assemblies and `ScanAssemblies(...)`; see [Troubleshooting](/docs/cross-cutting/troubleshooting#could-not-find-train-with-input-type-x)). `TrainException` if `metadata` is not `Pending`. `TrainAlreadyStartedException` (a `TrainException`, namespace `Trax.Effect.Exceptions`) if `metadata` says `Pending` but the stored row no longer is, because another execution started it first: the start is claimed with one conditional write in the store, so of two executions handed the same row only one runs the train. The refused one has run nothing and written nothing, and the row belongs to the other: do not record a failure on it. `OperationCanceledException` if the token is cancelled.

### RunAsync (void)

Executes the train registered for the input's type without returning a result.

```csharp
Task RunAsync(object trainInput, Metadata? metadata = null)
Task RunAsync(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

Parameters are identical to `RunAsync<TOut>`.

### RunByNameAsync

Runs the train registered under a name, rather than the one registered for the input's type. When two trains take the same input type, `RunAsync` reaches only one of them; `RunByNameAsync` runs the one you name.

```csharp
Task<TOut> RunByNameAsync<TOut>(string trainName, object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
Task RunByNameAsync(string trainName, object trainInput, CancellationToken cancellationToken, Metadata? metadata = null)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | N/A | The full name of the train's service type, `TrainRegistration.ServiceType.FullName`, which is also the name a run's metadata records (for example `MyApp.Trains.IProcessOrderTrain`). Only the exact full name matches. |
| `trainInput` | `object` | Yes | N/A | The input. It must be an instance of the train's input type. |
| `cancellationToken` | `CancellationToken` | Yes | N/A | Forwarded to the train's `Run` method. |
| `metadata` | `Metadata?` | No | `null` | As for `RunAsync`: a pre-created `Pending` record for the train to run as. |

**Throws**: `TrainException` if no discovered train has that name, if the input is not of its input type (both before anything is resolved), or if `metadata` is not `Pending`. Otherwise as `RunAsync`.

Like the rest of the bus it is an in-process call and checks no authorization. `ITrainExecutionService.RunAsync` authorizes the caller for the train it looked up and then runs that train through this method. Each call gets its own DI scope, as `RunAsync` does. The interface ships a default implementation that throws `NotSupportedException`, so a test double implementing `ITrainBus` keeps compiling; the bus `AddMediator` registers implements it. A host that replaces the bus with one keeping that default cannot run trains by name: `ITrainExecutionService.RunAsync` throws `NotSupportedException` before it writes any record (see [RunAsync](/docs/sdk-reference/mediator-api/train-execution#runasync)).

### InitializeTrain

Resolves (but does **not** run) the train for the given input type. It is public on `ITrainBus` but hidden from completion, and no Trax package calls it.

```csharp
object InitializeTrain(object trainInput)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `trainInput` | `object` | Yes | The input object used to discover the train |

**Returns**: The initialized train instance (as `object`).

**Throws**: [`NoTrainForInputException`](#notrainforinputexception) if no train is registered for the input's type.

## NoTrainForInputException

`Trax.Mediator.Exceptions.NoTrainForInputException`, an `InvalidOperationException`, is what `RunAsync` and `InitializeTrain` throw when no registered train takes the input's type. Before Trax.Mediator 1.24.0 they threw a `TrainException` with the same message.

```csharp
public class NoTrainForInputException : InvalidOperationException
{
    public NoTrainForInputException(Type inputType, IReadOnlyList<string> scannedAssemblies);
}
```

| Property | Type | Description |
|----------|------|-------------|
| `InputType` | `Type` | The runtime type of the input no registered train takes |
| `ScannedAssemblies` | `IReadOnlyList<string>` | The names of the assemblies the registry scanned for trains; empty when the registry does not scan |

The message is a host configuration error and names how the host is built, so it is for the host's log, not for a caller. That is why it is not a `TrainException`: Trax.Api's error filter and the scheduler's runner endpoints pass a `TrainException`'s message through as a train author's words, and they do not treat other exception types that way.

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

### Nested Trains

```csharp
// Inside a train junction
public class ProcessOrderJunction(ITrainBus trainBus) : Junction<OrderInput, OrderResult>
{
    public override async Task<OrderResult> Run(OrderInput input)
    {
        // Forward the junction's token so cancelling the parent reaches the child
        var paymentResult = await trainBus.RunAsync<PaymentResult>(
            new PaymentInput { Amount = input.Total },
            CancellationToken);

        return new OrderResult { PaymentId = paymentResult.Id };
    }
}
```

The child runs as a train of its own and is not linked to the parent run: its metadata's `ParentId` stays null. Passing the parent's `Metadata` as the `metadata` argument does not link them; it throws, because that argument must be a `Pending` record for the child to run as. See [Mediator: Nested Trains](/docs/mediator#nested-trains).

## Scope Isolation

Each `RunAsync` call creates a child DI scope. The train and all its dependencies are resolved from this scope, which is disposed asynchronously when the call returns, so a scoped dependency that implements only `IAsyncDisposable` is released without turning the run's result, or its own exception, into a disposal error. This means:

- **Blazor Server safe**: circuit-scoped services don't leak between train executions
- **Resource cleanup**: scoped services (`DbContext`, etc.) are disposed after each train
- **Nested isolation**: when a train dispatches another train via `ITrainBus`, the child train gets its own scope. Each train is a black box
- **Scheduler compatible**: the scheduler already creates per-job scopes; the additional child scope from `TrainBus` adds isolation for the actual train within the job runner's scope

`InitializeTrain` does **not** create a child scope. It resolves from the `TrainBus`'s own scope.

## Remarks

- Trains are discovered by input type at registration time (via [AddMediator](/docs/sdk-reference/configuration/add-mediator)). `RunAsync` and `InitializeTrain` reach one train per input type, the first scanned; `RunByNameAsync` reaches any registered train by name.
- The `metadata` parameter is for running a train as a record created beforehand, which is how the scheduler and the dashboard's ad-hoc run execute a train. It does not link a child run to a parent.
- `RunAsync` calls the train's `Run` method internally, which means exceptions are thrown (not returned as `Either`). Use try/catch for error handling.
- The `cancellationToken` overloads forward the token to `train.Run(input, cancellationToken)`, which propagates it to all steps. See [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens) for details.
