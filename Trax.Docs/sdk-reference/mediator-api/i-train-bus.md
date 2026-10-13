---
title: ITrainBus
description: "Reference for ITrainBus: RunAsync, RunByNameAsync and InitializeTrain, which run a train in process by its input type or its name, each in its own DI scope."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 1
package: Trax.Mediator
namespace: Trax.Mediator.Services.TrainBus
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainBus/ITrainBus.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainBus/TrainBus.cs
redirect_from:
  - /docs/sdk-reference/mediator-api/train-bus
---

# ITrainBus

Runs a train in process, chosen by the runtime type of its input or by its name. The bus checks no
authorization; [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
is the entry point that authorizes the caller first. `AddMediator` registers the bus as a scoped
service.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.TrainBus;

public interface ITrainBus
{
    Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null);
    Task<TOut> RunAsync<TOut>(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null);
    Task RunAsync(object trainInput, Metadata? metadata = null);
    Task RunAsync(object trainInput, CancellationToken cancellationToken, Metadata? metadata = null);

    Task<TOut> RunByNameAsync<TOut>(string trainName, object trainInput, CancellationToken cancellationToken, Metadata? metadata = null);
    Task RunByNameAsync(string trainName, object trainInput, CancellationToken cancellationToken, Metadata? metadata = null);

    object InitializeTrain(object trainInput);
}
```

## Members

| Member | Description |
|--------|-------------|
| [`RunAsync<TOut>`](#runasync) | Runs the train registered for the input's type and returns its output |
| [`RunAsync`](#runasync) | The same, discarding the output |
| [`RunByNameAsync`](#runbynameasync) | Runs the train registered under a name |
| [`InitializeTrain`](#initializetrain) | Resolves the train for the input's type without running it |

## RunAsync

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `trainInput` | `object` | required | The input. Its runtime type selects the train |
| `cancellationToken` | `CancellationToken` | none on the overloads without it | Forwarded to the train's `Run` and on to its junctions |
| `metadata` | `Metadata?` | `null` | A pre-created metadata record, in the `Pending` state, for the train to run as instead of creating its own. The scheduler and the dashboard's ad-hoc run pass one. It is not a parent link: the run's `ParentId` is not set |

**Returns**: the train's output (`Task<TOut>`), or a `Task` for the overloads without `TOut`.

**Throws**:

- [`NoTrainForInputException`](/docs/sdk-reference/mediator-api/exceptions) when no train is
  registered for the input's type. The message names the input type, the scanned assemblies and
  `ScanAssemblies(...)`; see
  [Troubleshooting](/docs/cross-cutting/troubleshooting#could-not-find-train-with-input-type-x).
- `TrainException` when `metadata` is in any state but `Pending`, including a running train's
  metadata.
- `TrainAlreadyStartedException` (a `TrainException`, in `Trax.Effect.Exceptions`) when `metadata`
  says `Pending` but the stored row no longer is, because another execution started it first. The
  start is claimed with one conditional write, so of two executions handed the same row only one
  runs the train. The refused one has run nothing and written nothing, and the row belongs to the
  other.
- `OperationCanceledException` when the token is cancelled.
- Whatever the train throws: `RunAsync` calls the train's `Run`, which throws rather than
  returning an `Either`.

The input-type lookup reaches one train per input type, the first one scanned.

## RunByNameAsync

Runs the train registered under `trainName`, rather than the one registered for the input's type.
When two trains take the same input type, `RunAsync` reaches only one of them; `RunByNameAsync`
reaches either.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `trainName` | `string` | required | The full name of the train's service type, `TrainRegistration.ServiceType.FullName`, which is also the name a run's metadata records (for example `MyApp.Trains.IProcessOrderTrain`). Only the exact full name matches |
| `trainInput` | `object` | required | The input, an instance of the train's input type |
| `cancellationToken` | `CancellationToken` | required | Forwarded to the train's `Run` |
| `metadata` | `Metadata?` | `null` | As for `RunAsync` |

**Returns**: the train's output, or a `Task`.

**Throws**: `TrainException` when no discovered train has that name or the input is not of its
input type (both before anything is resolved), or when `metadata` is not `Pending`. Otherwise as
`RunAsync`.

The interface gives `RunByNameAsync` a default body that throws `NotSupportedException`, so a test
double implementing `ITrainBus` compiles without it; the bus `AddMediator` registers implements
it. On a host whose replacement bus keeps the default, `ITrainExecutionService.RunAsync` throws
`NotSupportedException` before it writes any record (see
[RunAsync](/docs/sdk-reference/mediator-api/i-train-execution-service#runasync)).
`ITrainExecutionService.RunAsync` authorizes the caller for the train it looked up and then runs
that train through this method.

## InitializeTrain

Resolves, but does not run, the train for the input's type, from the bus's own scope. It carries
`[EditorBrowsable(EditorBrowsableState.Never)]`, so it is hidden from completion, and no Trax package calls it.

| Parameter | Type | Description |
|-----------|------|-------------|
| `trainInput` | `object` | The input whose type selects the train |

**Returns**: the train instance, as `object`.

**Throws**: [`NoTrainForInputException`](/docs/sdk-reference/mediator-api/exceptions) when no
train is registered for the input's type.

## Behavior

### Scope isolation

Each `RunAsync` and `RunByNameAsync` call creates a child DI scope. The train and its dependencies
are resolved from it, and it is disposed asynchronously when the call returns, so a scoped
dependency that implements only `IAsyncDisposable` is released without turning the run's result,
or its own exception, into a disposal error. As a result:

- Circuit-scoped services in Blazor Server do not leak between train executions.
- Scoped services (`DbContext` and the like) are disposed after each train.
- A train that dispatches another through `ITrainBus` gives the child its own scope.
- The scheduler creates a scope per job; the bus adds one more for the train inside it.

### Nested trains

A train run from inside another train's junction is a run of its own: its metadata's `ParentId`
stays null. Passing the parent's `Metadata` as `metadata` throws, because that argument must be a
`Pending` record for the child to run as. See [Mediator: Nested Trains](/docs/mediator#nested-trains).

## Examples

```csharp compile
public class OrderService(ITrainBus trainBus)
{
    public Task<OrderResult> ProcessOrder(OrderInput input) =>
        trainBus.RunAsync<OrderResult>(input);
}
```

```csharp compile
[ApiController]
public class OrderController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<IActionResult> ProcessOrder(OrderInput input, CancellationToken cancellationToken)
    {
        // The token reaches the train and every junction in it.
        var result = await trainBus.RunAsync<OrderResult>(input, cancellationToken);
        return Ok(result);
    }
}
```

A junction that runs another train, forwarding its own token so cancelling the parent reaches the
child:

```csharp compile
public class ProcessOrderJunction(ITrainBus trainBus) : Junction<OrderInput, OrderResult>
{
    public override async Task<OrderResult> Run(OrderInput input)
    {
        var payment = await trainBus.RunAsync<PaymentResult>(
            new PaymentInput { Amount = input.Total },
            CancellationToken);

        return new OrderResult { PaymentId = payment.Id };
    }
}
```

## See also

- [Mediator](/docs/mediator), how dispatch by input type works
- [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service), running a train by name with authorization
- [AddMediator](/docs/sdk-reference/configuration/add-mediator), which registers the trains the bus finds
- [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens)
