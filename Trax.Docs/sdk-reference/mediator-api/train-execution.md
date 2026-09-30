---
layout: default
title: TrainExecution
parent: Mediator API
grand_parent: SDK Reference
nav_order: 4
---

# TrainExecution

`ITrainExecutionService` provides programmatic train execution by name. Instead of resolving a specific train interface, you pass the train's service type name and a JSON string. The service handles discovery, deserialization, and dispatch. Train lookup matches by fully-qualified canonical name first (`ServiceType.FullName`), then by friendly name (`ServiceTypeName`). There is no short-name fallback.

It supports two execution paths:
- **Queue**: creates a WorkQueue entry for asynchronous dispatch by the scheduler.
- **Run**: executes the train synchronously via `ITrainBus` on the current machine.

A third method, `PrepareAsync`, does only the steps both paths start with (lookup, authorization, input reading), for a surface that submits the work some other way.

Registered automatically by `AddMediator()` as a scoped service.

## ITrainExecutionService

```csharp
public interface ITrainExecutionService
{
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        int priority = 0,
        DateTime? scheduledAt = null,
        CancellationToken ct = default
    );

    Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    );

    Task<PreparedTrain> PrepareAsync(
        string trainName,
        string? inputJson,
        CancellationToken ct = default
    );
}
```

## QueueAsync

Creates a WorkQueue entry for asynchronous execution. The scheduler picks up the entry on its next polling cycle and dispatches the train.

> `scheduledAt` was added before `ct`, and a JSON `null` input now throws `JsonException`. A call passing the token positionally after `priority` no longer compiles; name it (`ct: ct`). See [Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

```csharp
Task<QueueTrainResult> QueueAsync(
    string trainName,
    string? inputJson,
    int priority = 0,
    DateTime? scheduledAt = null,
    CancellationToken ct = default
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | N/A | Train name, matched by canonical name (`ServiceType.FullName`), then friendly name (`ServiceTypeName`). Prefer the fully-qualified interface name (e.g. `"MyApp.Trains.IProcessOrderTrain"`). |
| `inputJson` | `string?` | Yes | N/A | JSON-serialized input matching the train's `InputType`. Null or blank is read as an empty object, `{}`, which is refused when the input type needs values (see **Throws**). |
| `priority` | `int` | No | `0` | Dispatch priority (0-31, higher runs first) |
| `scheduledAt` | `DateTime?` | No | `null` | Earliest time the entry may be dispatched, stored as UTC. A `Local` value is converted; an `Unspecified` one is taken to already be UTC, which is how a timestamp without an offset arrives from JSON. Null dispatches as soon as a worker is free. |
| `ct` | `CancellationToken` | No | `default` | Cancellation token |

**Returns**: `QueueTrainResult`

| Property | Type | Description |
|----------|------|-------------|
| `WorkQueueId` | `long` | Database ID of the created WorkQueue entry |
| `ExternalId` | `string` | External ID assigned to the entry |

**Throws**:
- `TrainNotFoundException` (an `InvalidOperationException`) if no train is registered with the given name. Use `ITrainDiscoveryService.DiscoverTrains()` to list available trains.
- `AmbiguousTrainNameException` if the name matches more than one train's friendly name.
- `TrainInputValidationException` if `inputJson` exceeds the configured size cap (`WithMaxInputJsonBytes`, 256 KiB by default), or if the input as it would be stored (step 4) is larger than `TrainInputReader.StoredInputGrowthFactor` (4) times that cap. For the second, `MaxBytes` is the stored cap and `ObservedBytes` the stored size.
- `JsonException` if `inputJson` does not deserialize to the train's input type, or names a property twice (`{"amount":1,"Amount":999}`). That includes a null or blank `inputJson` for an input type that needs values: a constructor parameter with no default (a positional record such as `record RenamePlayer(string Id, string NewName)`) or a `required` member. It fails here, at enqueue, rather than queueing a run whose input is full of nulls. `Unit`, an input with only settable properties, and parameters with defaults are built from `{}` as before, and an explicit `"{}"` is read like any other input.
- `JsonException` if `inputJson` is the JSON literal `null`, which is well-formed but is not an input, or is blank and the input type needs values, as for `QueueAsync`.
- `TrainAuthorizationException` if the train has `[TraxAuthorize]` requirements the caller does not meet. Authorization runs before the input is read, and applies to every caller-built enqueue, including the operations surface (`queueTrain`, `requeueExecution`). The dashboard's queue dialog and re-queue button also route through this method, but inside a trusted execution scope, so per-train requirements are skipped there; the dashboard is gated by its host. `Trax.Docs/adr/0017` records why.
- `TrainAuthorizationNotConfiguredException` (an `InvalidOperationException`, carrying `TrainName`) if the train declares `[TraxAuthorize]` and no `ITrainAuthorizationService` is registered, unless the call runs inside a trusted execution scope. It is a host misconfiguration rather than a refusal of the caller's input, and the type lets a caller report it that way without reading the message. In a hosted app this rarely fires, because `AuthorizationRegistrationValidator` already refuses to start such a host; the runtime check covers hosts where hosted services do not run, such as the Lambda runner. The check fails closed; a host that serves no API submissions opts out with `AddMediator(m => m.AllowMissingAuthorizationService())`, after which the missing service is a no-op.
- Any exception thrown by the train's `QueueSubjectKey` override. A key that cannot be computed aborts the enqueue rather than becoming null.
- `InvalidOperationException` if `QueueSubjectKey` returns an empty string, a key that is only whitespace, a key containing an unpaired surrogate, or a key longer than 512 Unicode characters (an emoji counts once, although it is two UTF-16 units; from Trax.Mediator 1.23.0, which leaves these rules to `WorkQueue.Create` and wraps its `ArgumentException` as the inner exception). Return null for an entry that should not be serialized.
- Any exception thrown by the train's [`OnQueue`](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) hook, if the train overrides it. A throw aborts the enqueue and leaves no entry behind: on the default path the hook runs before the entry is committed, and for a train that defers promotion the already-staged entry is removed (only if it is still staged, never once promoted or dispatched), whether or not the caller has cancelled. A failure to remove it does not replace the hook's exception; the stale staged entry sweep resolves an entry left behind.
- `QueueHookTimeoutException` (an `InvalidOperationException`, carrying `TrainName` and `Limit`) from Trax.Mediator 1.23.0, when a train that does not defer promotion runs its `OnQueue` hook longer than `MaxQueueHookDuration` (30 seconds by default; `AddMediator(m => m.WithMaxQueueHookDuration(TimeSpan))` changes it, and `Timeout.InfiniteTimeSpan` removes it). The hook's token is cancelled at the limit and the enqueue stops waiting whether or not the hook stops: it rolls back, so no entry is written and nothing the hook wrote on `IEnqueueContextAccessor.Current` is kept, even a write the hook had already saved, and the connection goes back to the pool. A hook that ignores its token keeps running, but an enqueue it starts after that is refused rather than committed on its own. `Trax.Mediator/docs/adr/0004` records the reasoning.
- `QueuedWorkCancelledException` (an `InvalidOperationException`, carrying `WorkQueueId` and `TrainName`) for a train that defers promotion, when its staged entry was cancelled while the hook ran (by an operator, or by the stale staged entry sweep because the hook outlived `StaleStagedEntryTimeout`). If the sweep promoted it instead (`PromoteStaleStagedEntries()`), the entry will run and `QueueAsync` succeeds. When it throws, the work will not run, but the hook's side-effect may already have been applied, and the message says so.

### What it does

1. Looks up the train by `trainName` via `ITrainDiscoveryService`.
2. Authorizes the caller against the train's requirements, failing closed as described under **Throws**.
3. Deserializes `inputJson` to the train's `InputType` through [`TrainInputReader.Read`](#traininputreader), reading null or blank as `{}`, so `OnQueue` and `QueueSubjectKey` always receive a real input. Property names are matched whatever their case, so `{"Amount":5}` and `{"amount":5}` are the same input, and a property given twice, in the same or another casing, is refused with `JsonException` (`Trax.Docs/adr/0023`). JSON reference metadata is not honoured: a list written as `{"$id":"1","$values":[...]}` is refused with `JsonException`, and `$ref` is read as an unknown property, not as a reference to another part of the input.
4. Re-serializes the input using manifest serialization options (normalizes the JSON: indented, every member written), and refuses the enqueue with `TrainInputValidationException` when that stored form is larger than 4 times `MaxInputJsonBytes`. The check runs before the entry exists, so nothing is written.
5. Creates a `WorkQueue` entry with the train name, serialized input, input type name, priority, and `scheduledAt` converted to UTC.
6. Stamps the entry's subject key from the train's [`QueueSubjectKey`](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) override, if it has one. An exception from `QueueSubjectKey`, or an empty, whitespace-only or over-long key, aborts the enqueue, so no entry is written.
7. Tracks the entry, then (if the train overrides [`OnQueue`](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook)) enters the enqueue context and invokes the hook with the entry's `ExternalId` and the input, then saves and commits, all in one transaction. A throw rolls the whole thing back, so nothing the hook tracked on `IEnqueueContextAccessor.Current` survives either. Trains that do not override the hook are never resolved here, enter no context, and open no transaction: their enqueue is a single write.
8. Returns the entry's ID and external ID.

The train that steps 6 and 7 call is resolved once, on first use, in a DI scope the enqueue creates and disposes before it returns, never from the caller's scope. A caller that holds its scope for a long time, such as a Blazor circuit, therefore keeps no train alive between enqueues, and the scoped services a hook takes are fresh for each enqueue.

Tracking the entry before the hook runs does not insert it (`Track` is change tracking only), so the hook still runs before the row exists, as its contract states.

When the train sets [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable), the shape changes to three steps instead: the entry is committed **unconfirmed** and undispatchable, the hook runs outside that transaction, and a second commit promotes it. A throwing hook removes the staged entry if it is still staged, so the observable contract is the same. Once the hook has returned the mutation counts as accepted, so the promotion runs even if the caller cancels. If the entry was cancelled while the hook ran, the promotion finds nothing to confirm and `QueueAsync` throws `QueuedWorkCancelledException` instead of reporting success; if something else already confirmed it (the sweep, with promotion opted in), it will run and `QueueAsync` succeeds. `IEnqueueContextAccessor.Current` is null inside such a hook, because the entry is already committed and there is no transaction to join. A crash between the two commits leaves the entry unconfirmed, and the scheduler's [stale staged entry sweep](/docs/scheduler/admin-trains/manifest-manager#resolvestalestagedentriesjunction) cancels it (or promotes it, if the host opted in) once it is older than `StaleStagedEntryTimeout`. The sweep runs in the ManifestManager, so it does not run while the ManifestManager is disabled (`SchedulerConfiguration.ManifestManagerEnabled = false`, also the dashboard's Server Settings switch); some host sharing the database must run it.

An enqueue started from inside another train's `OnQueue` hook, while that enqueue's transaction is open, takes a different path: it tracks its entry on the outer enqueue's context, runs its own hook, and flushes the entry inside the outer transaction, so it commits or rolls back with the outer entry and uses no connection of its own. A deferring train on this path is written confirmed rather than staged. If the nested enqueue fails, the outer one fails too, even when the hook catches the exception; if the hook returns while a nested enqueue it started is still running, the outer enqueue throws `InvalidOperationException`. See [OnQueue](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook).

On the in-memory provider, beginning the transaction succeeds but returns one whose commit and rollback do nothing: the provider ignores EF's `TransactionIgnoredWarning`. The queue row and anything the hook tracked on `IEnqueueContextAccessor.Current` still land together in one `SaveChanges`, and because a transaction object exists, an enqueue nested in a hook finds one to join as it would on a relational provider. A provider whose `BeginTransaction` throws `InvalidOperationException` or `NotSupportedException` gets no transaction at all, with the same single `SaveChanges`.

## RunAsync

Executes a train synchronously via `ITrainBus`. This is a blocking call that returns when the train completes. It creates no work queue entry, so it does not fire `OnQueue` and does not consult `QueueSubjectKey`: a synchronous run can overlap queued work for the same subject.

```csharp
Task<RunTrainResult> RunAsync(
    string trainName,
    string inputJson,
    CancellationToken ct = default
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | N/A | Train name (matched by canonical name, then friendly name) |
| `inputJson` | `string` | Yes | N/A | JSON-serialized input matching the train's `InputType`. Blank is read the way `QueueAsync` reads a missing input |
| `ct` | `CancellationToken` | No | `default` | Cancellation token forwarded to the train's `Run` |

**Returns**: `RunTrainResult`

| Property | Type | Description |
|----------|------|-------------|
| `MetadataId` | `long` | Database ID of the metadata record for this execution |
| `Output` | `object?` | The train's typed output. `null` for `Unit` trains; the actual output object for trains with a typed `TOut` parameter. |

**Throws**:
- `TrainNotFoundException` (an `InvalidOperationException`) if no train is registered with the given name, or `AmbiguousTrainNameException` if the name matches more than one train's friendly name.
- `TrainInputValidationException` if `inputJson` exceeds the configured size cap.
- `JsonException` if `inputJson` is the JSON literal `null`, which is well-formed but is not an input.
- `TrainException` if the train itself fails during execution (propagated from `ITrainBus`).
- `InvalidOperationException` from the DI container if the train cannot be built, for example because a constructor dependency is not registered. The train is resolved before its metadata is written, so this leaves no `Pending` record behind.
- `TrainAuthorizationException` if the train has `[TraxAuthorize]` requirements the caller does not meet.
- `TrainAuthorizationNotConfiguredException` (an `InvalidOperationException`) if the train declares `[TraxAuthorize]` and no `ITrainAuthorizationService` is registered, unless the call runs inside a trusted execution scope or the host called `AllowMissingAuthorizationService()`. The same fail-closed rule, and the same trusted-scope exemption, as `QueueAsync`.

### What it does

1. Looks up the train by `trainName` via `ITrainDiscoveryService`.
2. Authorizes the caller against the train's requirements, failing closed as described under **Throws**.
3. Deserializes `inputJson` to the train's `InputType`, reading blank, casing, repeated properties and reference metadata as `QueueAsync` does.
4. Resolves the train found in step 1 by its canonical name, in a child DI scope. The train that runs is the one that was authorized, even when another train takes the same input type. A train that cannot be built throws here, before anything is written.
5. Creates a `Metadata` record with a generated external ID and persists it in the `Pending` state.
6. Runs the train as that record, for the train's `OutputType`, invoked by reflection.
7. Returns the metadata ID and the train's output (or `null` for `Unit` trains). The output is read through a generic method closed over `OutputType`, so an output type that is not public (an `internal` record in the consumer's assembly, say) is returned like any other.

Steps 4 to 6 are the default `LocalRunExecutor` with the default `ITrainBus`. A host that registers its own `ITrainBus` gets the record written first, then `ITrainBus.RunByNameAsync<TOut>(trainName, input, ct, metadata)` called with it. A remote executor (`UseRemoteRun`, `UseLambdaRun`) takes over steps 4 to 7 and does them its own way.

## PrepareAsync

Resolves a train by name, authorizes the current caller for it, and reads the caller's input into the train's input type. These are steps 1 to 3 of `QueueAsync` and `RunAsync`, which call the same code, so a surface that submits work itself (the scheduler's run operation, say) accepts and refuses exactly what a queue or a run does. Nothing is written.

```csharp
Task<PreparedTrain> PrepareAsync(
    string trainName,
    string? inputJson,
    CancellationToken ct = default
)

public sealed class PreparedTrain
{
    public TrainRegistration Registration { get; }
    public object Input { get; }   // an instance of Registration.InputType, never null
}
```

Authorization runs before the input is read, so a caller who may not use the train learns nothing about its input from a parse error. The input is read as `QueueAsync` reads it: null or blank as `{}`, property names in any case, a repeated property refused, and the `MaxInputJsonBytes` cap applied.

`PreparedTrain` has no public constructor: the only way to get one is from `PrepareAsync`, so code holding one knows the authorization check ran. An implementation of `ITrainExecutionService` written before this method existed inherits a default that throws `NotSupportedException` rather than skipping the check.

### Throws

The same as `RunAsync`: `TrainNotFoundException`, `AmbiguousTrainNameException`, `UnauthorizedAccessException`, `TrainInputValidationException`, `JsonException`, and `InvalidOperationException` for a `[TraxAuthorize]` train on a host with no `ITrainAuthorizationService` outside a trusted scope.


## TrainInputReader

The reading rules above, as a public static class in `Trax.Mediator.Services.TrainExecution`. `QueueAsync`, `RunAsync` and `PrepareAsync` all read input through it; a host or package that takes train input JSON on a path of its own should call it rather than copy the rules, so it accepts and refuses the same JSON.

```csharp
public static class TrainInputReader
{
    public const int StoredInputGrowthFactor = 4;

    public static object Read(
        string? inputJson,
        TrainRegistration registration,
        int maxInputJsonBytes
    );
}
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `inputJson` | `string?` | Yes | N/A | The caller's JSON. Null or blank is read as `{}` |
| `registration` | `TrainRegistration` | Yes | N/A | The train whose `InputType` is read |
| `maxInputJsonBytes` | `int` | Yes | N/A | The size cap in UTF-8 bytes, normally `MediatorConfiguration.MaxInputJsonBytes` |

**Returns**: an instance of `registration.InputType`, never null.

**Throws**: `TrainInputValidationException` if the JSON is larger than `maxInputJsonBytes` (checked before parsing); `JsonException` if it cannot be read as the input type, names a property twice, uses `$id`/`$values` reference metadata where the type expects a list, is the literal `null`, or is blank for an input type that needs values.

It does not authorize. Call it after the caller has been authorized for the train, as `PrepareAsync` does, so a caller who may not use the train learns nothing about its input from a parse error.

`StoredInputGrowthFactor` is how many times `MaxInputJsonBytes` a queued input's stored form may be (see step 4 of `QueueAsync`).

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
            ct: ct);

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

`RunAsync` supports per-train and global concurrency limits to prevent overloading remote backends. When a limit is reached, additional requests wait in-process until a slot opens. No requests are rejected.

See [Concurrency Limiting](/docs/sdk-reference/mediator-api/concurrency-limiting) for configuration details.

## Package

```
dotnet add package Trax.Mediator
```
