---
layout: default
title: TrainExecution
parent: Mediator API
grand_parent: SDK Reference
nav_order: 4
---

# TrainExecution

`ITrainExecutionService` provides programmatic train execution by name. Instead of resolving a specific train interface, you pass the train's service type name and a JSON string — the service handles discovery, deserialization, and dispatch. Train lookup matches by fully-qualified canonical name first (`ServiceType.FullName`), then friendly name (`ServiceTypeName`), then short name (`ServiceType.Name`).

It supports two execution paths:
- **Queue** — creates a WorkQueue entry for asynchronous dispatch by the scheduler.
- **Run** — executes the train synchronously via `ITrainBus` on the current machine.

Registered automatically by `AddMediator()` as a scoped service.

## ITrainExecutionService

```csharp
public interface ITrainExecutionService
{
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string inputJson,
        int priority = 0,
        CancellationToken ct = default
    );

    Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    );
}
```

## QueueAsync

Creates a WorkQueue entry for asynchronous execution. The scheduler picks up the entry on its next polling cycle and dispatches the train.

```csharp
Task<QueueTrainResult> QueueAsync(
    string trainName,
    string inputJson,
    int priority = 0,
    CancellationToken ct = default
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | — | Train name — matched by canonical name (`ServiceType.FullName`), then friendly name (`ServiceTypeName`), then short name (`ServiceType.Name`). Prefer the fully-qualified interface name (e.g. `"MyApp.Trains.IProcessOrderTrain"`). |
| `inputJson` | `string` | Yes | — | JSON-serialized input matching the train's `InputType` |
| `priority` | `int` | No | `0` | Dispatch priority (0-31, higher runs first) |
| `ct` | `CancellationToken` | No | `default` | Cancellation token |

**Returns**: `QueueTrainResult`

| Property | Type | Description |
|----------|------|-------------|
| `WorkQueueId` | `long` | Database ID of the created WorkQueue entry |
| `ExternalId` | `string` | External ID assigned to the entry |

**Throws**:
- `InvalidOperationException` — if no train is registered with the given name. The message includes a hint to use `ITrainDiscoveryService.DiscoverTrains()` to list available trains.
- `InvalidOperationException` — if JSON deserialization returns null.
- `TrainAuthorizationException` — if the train has `[TraxAuthorize]` requirements that the current user does not satisfy. Only applies when `ITrainAuthorizationService` is registered (i.e., the API layer is in use).

### What it does

1. Looks up the train by `trainName` via `ITrainDiscoveryService`.
2. If an `ITrainAuthorizationService` is registered, checks the user against the train's authorization requirements. Throws on failure.
3. Deserializes `inputJson` to the train's `InputType`.
4. Re-serializes the input using manifest serialization options (normalizes the JSON).
5. Creates a `WorkQueue` entry with the train name, serialized input, input type name, and priority.
6. Persists the entry via the data context.
7. Returns the entry's ID and external ID.

## RunAsync

Executes a train synchronously via `ITrainBus`. This is a blocking call that returns when the train completes.

```csharp
Task<RunTrainResult> RunAsync(
    string trainName,
    string inputJson,
    CancellationToken ct = default
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | — | Train name (matched by canonical name, then friendly name, then short name) |
| `inputJson` | `string` | Yes | — | JSON-serialized input matching the train's `InputType` |
| `ct` | `CancellationToken` | No | `default` | Cancellation token forwarded to `ITrainBus.RunAsync` |

**Returns**: `RunTrainResult`

| Property | Type | Description |
|----------|------|-------------|
| `MetadataId` | `long` | Database ID of the metadata record for this execution |
| `Output` | `object?` | The train's typed output. `null` for `Unit` trains; the actual output object for trains with a typed `TOut` parameter. |

**Throws**:
- `InvalidOperationException` — if no train is registered with the given name.
- `InvalidOperationException` — if JSON deserialization returns null.
- `TrainException` — if the train itself fails during execution (propagated from `ITrainBus`).
- `TrainAuthorizationException` — if the train has `[TraxAuthorize]` requirements that the current user does not satisfy. Only applies when `ITrainAuthorizationService` is registered.

### What it does

1. Looks up the train by `trainName` via `ITrainDiscoveryService`.
2. If an `ITrainAuthorizationService` is registered, checks the user against the train's authorization requirements. Throws on failure.
3. Deserializes `inputJson` to the train's `InputType`.
4. Creates a `Metadata` record with a generated external ID.
5. Persists the metadata via the data context.
6. Calls the typed `ITrainBus.RunAsync<TOut>(input, ct, metadata)` via reflection, using the train's `OutputType` from its registration.
7. Returns the metadata ID and the train's output (or `null` for `Unit` trains).

## Examples

### Queue a train for async dispatch

```csharp
public class OrderController(ITrainExecutionService execution) : ControllerBase
{
    [HttpPost("orders/queue")]
    public async Task<IActionResult> QueueOrder(
        [FromBody] JsonElement input,
        CancellationToken ct)
    {
        var result = await execution.QueueAsync(
            "MyApp.Trains.IProcessOrderTrain",
            input.GetRawText(),
            priority: 5,
            ct);

        return Accepted(new { result.WorkQueueId, result.ExternalId });
    }
}
```

### Run a train synchronously

```csharp
public class OrderController(ITrainExecutionService execution) : ControllerBase
{
    [HttpPost("orders/run")]
    public async Task<IActionResult> RunOrder(
        [FromBody] JsonElement input,
        CancellationToken ct)
    {
        var result = await execution.RunAsync(
            "MyApp.Trains.IProcessOrderTrain",
            input.GetRawText(),
            ct);

        return Ok(new { result.MetadataId });
    }
}
```

### Discover available trains first

```csharp
public class TrainController(
    ITrainDiscoveryService discovery,
    ITrainExecutionService execution
) : ControllerBase
{
    [HttpPost("trains/{trainName}/run")]
    public async Task<IActionResult> RunByName(
        string trainName,
        [FromBody] JsonElement input,
        CancellationToken ct)
    {
        // Validate the train exists before attempting execution
        var trains = discovery.DiscoverTrains();
        var match = trains.FirstOrDefault(t => t.ServiceType.FullName == trainName);

        if (match is null)
            return NotFound($"No train registered with name '{trainName}'");

        var result = await execution.RunAsync(trainName, input.GetRawText(), ct);
        return Ok(new { result.MetadataId });
    }
}
```

## Concurrency Limiting

`RunAsync` supports per-train and global concurrency limits to prevent overloading remote backends. When a limit is reached, additional requests wait in-process until a slot opens — no requests are rejected.

See [Concurrency Limiting](/docs/sdk-reference/mediator-api/concurrency-limiting) for configuration details.

## Package

```
dotnet add package Trax.Mediator
```
