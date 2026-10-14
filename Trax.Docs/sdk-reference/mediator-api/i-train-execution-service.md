---
title: ITrainExecutionService
description: "Reference for ITrainExecutionService: QueueAsync, RunAsync and PrepareAsync, which queue or run a train by name from JSON input after authorizing the caller."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 3
package: Trax.Mediator
namespace: Trax.Mediator.Services.TrainExecution
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/ITrainExecutionService.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainExecutionService.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/PreparedTrain.cs
redirect_from:
  - /docs/sdk-reference/mediator-api/train-execution
---

# ITrainExecutionService

Queues or runs a train by name, from a JSON input. The service looks the train up, authorizes the
caller for it, reads the input, and then either writes a work queue entry for the scheduler or runs
the train through the registered `IRunExecutor`: on this machine through `ITrainBus` by default, or
on a remote endpoint under [`UseRemoteRun`](/docs/sdk-reference/scheduler-api/use-remote-run) or
[`UseLambdaRun`](/docs/sdk-reference/scheduler-api/use-lambda-run). `PrepareAsync` does only the
steps both paths start with, for a surface that submits the work some other way. `AddMediator()`
registers the default implementation, `TrainExecutionService`, as a scoped service.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.TrainExecution;

public interface ITrainExecutionService
{
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        int priority = 0,
        DateTime? scheduledAt = null,
        CancellationToken ct = default
    );

    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        QueueTrainOptions options,
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

public record QueueTrainResult(long WorkQueueId, string ExternalId);

public record RunTrainResult(long MetadataId, string ExternalId, object? Output = null);

public sealed record QueueTrainOptions
{
    public int Priority { get; init; }
    public DateTime? ScheduledAt { get; init; }
    public long? ReplayDecisionsOf { get; }
    public long? ResumeFrom { get; }
    public string? ResumeAt { get; }
}

public sealed class PreparedTrain
{
    public TrainRegistration Registration { get; }
    public object Input { get; }
}
```

## Members

| Member | Description |
|--------|-------------|
| [`QueueAsync`](#queueasync) | Writes a work queue entry the scheduler dispatches |
| [`QueueAsync` with `QueueTrainOptions`](#queueasync-with-queuetrainoptions) | The same, with the options the first overload has no parameter for |
| [`RunAsync`](#runasync) | Runs the train now and returns its output |
| [`PrepareAsync`](#prepareasync) | Looks up, authorizes and reads the input, and writes nothing |

Train lookup, in every member, matches the fully-qualified canonical name first
(`ServiceType.FullName`), then the friendly name (`ServiceTypeName`). There is no short-name
fallback.

## QueueAsync

Creates a work queue entry. The scheduler picks it up on its next polling cycle and dispatches the
train.

> `scheduledAt` was added before `ct`, and a JSON `null` input throws `JsonException`. A call
> passing the token positionally after `priority` no longer compiles; name it (`ct: ct`). See
> [Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `trainName` | `string` | required | The train's name, matched by canonical name, then friendly name (for example `"MyApp.Trains.IProcessOrderTrain"`) |
| `inputJson` | `string?` | required | JSON of the train's `InputType`. Null or blank is read as an empty object, `{}`, which is refused when the input type needs values |
| `priority` | `int` | `0` | Dispatch priority, 0 to 31, higher first |
| `scheduledAt` | `DateTime?` | `null` | The earliest time the entry may be dispatched, stored as UTC. A `Local` value is converted; an `Unspecified` one is taken to be UTC already, which is how a timestamp without an offset arrives from JSON. Null dispatches as soon as a worker is free |
| `ct` | `CancellationToken` | `default` | Cancels the enqueue |

**Returns**: `QueueTrainResult`, with `WorkQueueId` (the entry's database id) and `ExternalId`
(the external id assigned to the entry, which the run later executes under).

**Throws**:

- `TrainNotFoundException` when no train is registered with the name, and
  `AmbiguousTrainNameException` when it matches more than one train's friendly name (see
  [Mediator exceptions](/docs/sdk-reference/mediator-api/exceptions)).
- `TrainInputValidationException` when `inputJson` is over the size cap (`WithMaxInputJsonBytes`,
  256 KiB by default), or the input as it would be stored (step 4 below) is larger than
  `TrainInputReader.StoredInputGrowthFactor` (4) times that cap. For the second, `MaxBytes` is the
  stored cap and `ObservedBytes` how much of the stored form had been written when writing
  stopped, which is more than the cap but not the stored form's full size.
- `JsonException` when `inputJson` does not deserialize to the train's input type, names a
  property twice (`{"amount":1,"Amount":999}`), is the JSON literal `null`, or is null or blank for
  an input type that needs values: a constructor parameter with no default (a positional record
  such as `record RenamePlayer(string Id, string NewName)`) or a `required` member. `Unit`, an input
  with only settable properties, and parameters with defaults are built from `{}`.
- `TrainAuthorizationException` (from Trax.Api, in `Trax.Api.Exceptions`, an
  `UnauthorizedAccessException` thrown by its `ITrainAuthorizationService`) when the train's
  `[TraxAuthorize]` requirements refuse the caller. Authorization runs before the input is read,
  and applies to every caller-built enqueue, including the operations surface (`queueTrain`,
  `requeueExecution`). The dashboard's queue dialog and re-queue button also go through this
  method, inside a trusted execution scope, so per-train requirements are skipped there; the
  dashboard is gated by its host. `Trax.Docs/adr/0017` records why.
- `TrainAuthorizationNotConfiguredException` when the train declares `[TraxAuthorize]` and no
  `ITrainAuthorizationService` is registered, unless the call runs inside a trusted execution scope
  or the host called `AddMediator(m => m.AllowMissingAuthorizationService())`. The mediator's
  startup authorization check already refuses to start such a host; the runtime check covers hosts
  where hosted services do not run, such as the Lambda runner.
- Any exception the train's `QueueSubjectKey` override throws: a key that cannot be computed
  aborts the enqueue rather than becoming null.
- `InvalidOperationException` when `QueueSubjectKey` returns an empty string, a key that is only
  whitespace, a key containing a NUL character or an unpaired surrogate, or a key longer than 512
  Unicode characters (an emoji counts once). From Trax.Mediator 1.23.0 the rules are
  `WorkQueue.Create`'s, and its `ArgumentException` is the inner exception.
- Any exception the train's [`OnQueue`](/docs/mediator/enqueue-hooks)
  hook throws. The enqueue is aborted and leaves no entry: on the default path the hook runs before
  the entry is committed, and for a train that defers promotion the staged entry is removed (only
  while it is still staged, never once promoted or dispatched), whether or not the caller has
  cancelled. A failure to remove it does not replace the hook's exception; the stale staged entry
  sweep resolves an entry left behind.
- `QueueHookTimeoutException` (from Trax.Mediator 1.23.0) when a train that does not defer
  promotion runs its `OnQueue` hook longer than `MaxQueueHookDuration` (30 seconds by default;
  `WithMaxQueueHookDuration(TimeSpan)` changes it, `Timeout.InfiniteTimeSpan` removes it). The
  hook's token is cancelled at the limit and the enqueue stops waiting whether or not the hook
  stops: it rolls back, so no entry is written and nothing the hook wrote on
  `IEnqueueContextAccessor.Current` is kept, even a write the hook had already saved, and the
  connection goes back to the pool. A hook that ignores its token keeps running, but an enqueue it
  starts after that is refused rather than committed on its own. `Trax.Mediator/docs/adr/0004`
  records the reasoning.
- `QueuedWorkCancelledException` for a train that defers promotion, when its staged entry was
  cancelled while the hook ran (by an operator, or by the stale staged entry sweep because the hook
  outlived `StaleStagedEntryTimeout`). If the sweep promoted it instead
  (`PromoteStaleStagedEntries()`), the entry will run and `QueueAsync` succeeds. When it throws,
  the work will not run, but the hook's side-effect may already have been applied, and the message
  says so.

### What it does

1. Looks up the train by `trainName` through `ITrainDiscoveryService`.
2. Authorizes the caller against the train's requirements, failing closed as described under
   **Throws**.
3. Reads `inputJson` into the train's `InputType` through
   [`TrainInputReader.Read`](/docs/sdk-reference/mediator-api/train-input-reader#read), so
   `OnQueue` and `QueueSubjectKey` always receive a real input. Property names match whatever their
   case, a property given twice is refused, and JSON reference metadata (`$id`, `$values`, `$ref`)
   is not honoured. A run's saved input carries that metadata, so a re-queue resolves it first with
   [`TrainInputReader.ResolveSavedInput`](/docs/sdk-reference/mediator-api/train-input-reader#resolvesavedinput).
4. Re-serializes the input with the manifest serialization options (indented, every member
   written), and refuses the enqueue with `TrainInputValidationException` when that stored form is
   larger than 4 times `MaxInputJsonBytes`. The bytes are counted as they are written and writing
   stops the moment the cap is crossed, so a stored form far over the cap is never built in full.
   The check runs before the entry exists, so nothing is written.
5. Creates a `WorkQueue` entry with the train name, serialized input, input type name, priority,
   and `scheduledAt` converted to UTC.
6. Stamps the entry's subject key from the train's
   [`QueueSubjectKey`](/docs/mediator/queued-work#two-mutations-for-one-record)
   override, if it has one.
7. Tracks the entry, then, if the train overrides `OnQueue`, enters the enqueue context and calls
   the hook with the entry's metadata (its `ExternalId` and input), then saves and commits, all in
   one transaction. A throw rolls the whole thing back, including anything the hook tracked on
   `IEnqueueContextAccessor.Current`. A train that does not override the hook is never resolved
   here, enters no context and opens no transaction: its enqueue is a single write.
8. Returns the entry's id and external id.

The train that steps 6 and 7 call is resolved once, on first use, in a DI scope the enqueue creates
and disposes before it returns, never from the caller's scope. A caller that holds its scope for a
long time, such as a Blazor circuit, keeps no train alive between enqueues, and the scoped services
a hook takes are fresh for each enqueue. Tracking the entry before the hook runs does not insert it
(`Track` is change tracking only), so the hook still runs before the row exists.

A train that sets [`DeferQueuePromotion`](/docs/mediator/queued-work#accepting-a-mutation-is-two-writes)
takes three steps instead: the entry is committed unconfirmed and undispatchable, the hook runs
outside that transaction, and a second commit promotes it. A throwing hook removes the staged entry
if it is still staged. Once the hook has returned the mutation counts as accepted, so the promotion
runs even if the caller cancels. If the entry was cancelled while the hook ran, the promotion finds
nothing to confirm and `QueueAsync` throws `QueuedWorkCancelledException`; if something else
already confirmed it (the sweep, with promotion opted in), it will run and `QueueAsync` succeeds.
`IEnqueueContextAccessor.Current` is null inside such a hook. A crash between the two commits
leaves the entry unconfirmed, and the scheduler's
[stale staged entry sweep](/docs/scheduler/admin-trains/manifest-manager#resolvestalestagedentriesjunction)
cancels it (or promotes it, if the host opted in) once it is older than `StaleStagedEntryTimeout`.
The sweep runs in the ManifestManager, so it does not run while the ManifestManager is disabled
(`SchedulerConfiguration.ManifestManagerEnabled = false`, also the dashboard's Server Settings
switch); some host sharing the database must run it.

An enqueue started from inside another train's `OnQueue` hook, while that enqueue's transaction is
open, tracks its entry on the outer enqueue's context, runs its own hook, and flushes the entry
inside the outer transaction, so it commits or rolls back with the outer entry and uses no
connection of its own. A deferring train on this path is written confirmed rather than staged. If
the nested enqueue fails, the outer one fails too, even when the hook catches the exception; if the
hook returns while a nested enqueue it started is still running, the outer enqueue throws
`InvalidOperationException`.

On the in-memory provider, beginning the transaction succeeds but returns one whose commit and
rollback do nothing: the provider ignores EF's `TransactionIgnoredWarning`. The queue row and
anything the hook tracked on `IEnqueueContextAccessor.Current` still land together in one
`SaveChanges`, and an enqueue nested in a hook finds a transaction to join as it would on a
relational provider. A provider whose `BeginTransaction` throws `InvalidOperationException` or
`NotSupportedException` gets no transaction at all, with the same single `SaveChanges`.

### QueueAsync with QueueTrainOptions

Queues exactly as the overload above does, reads the input and authorizes the same way, and throws
the same exceptions for the same reasons.

| `QueueTrainOptions` property | Type | Default | Description |
|---|---|---|---|
| `Priority` | `int` | `0` | Dispatch priority, 0 to 31, higher first |
| `ScheduledAt` | `DateTime?` | `null` | The earliest dispatch time, read as `scheduledAt` above |
| `ReplayDecisionsOf` | `long?` | `null` | The metadata id of an earlier run whose recorded [decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions) the new run replays, so it takes the tracks that run took. Set by Trax only: [`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync) sets it to the run being re-queued when that run has decisions to replay |
| `ResumeFrom` | `long?` | `null` | The metadata id of a failed run the new run resumes, from the step after its latest checkpoint (or `ResumeAt`). Set by Trax only, by [`IOperationsService.ResumeExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#resumeexecutionasync) |
| `ResumeAt` | `string?` | `null` | The step a resumed run resumes at, or null for after the latest checkpoint. Set by Trax only |

An `ITrainExecutionService` written before this overload existed, a custom one or a decorator
around the mediator's, gets a default implementation that queues through the overload above. When
`ReplayDecisionsOf` is set, that default throws `DecisionReplayNotSupportedException`, carrying the
implementation's type, rather than queue a run that would ask its deciders afresh.

## RunAsync

Runs a train now, through the registered `IRunExecutor` (on this machine through `ITrainBus` unless
a remote executor is configured), and returns when the train completes. It creates no work queue
entry, so it does not fire `OnQueue` and does not consult `QueueSubjectKey`: a direct run can
overlap queued work for the same subject. It is subject to the
[concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting), which make it wait
rather than refuse.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `trainName` | `string` | required | The train's name, matched by canonical name, then friendly name |
| `inputJson` | `string` | required | JSON of the train's `InputType`. Blank is read as `QueueAsync` reads a missing input |
| `ct` | `CancellationToken` | `default` | Forwarded to the train's `Run` |

**Returns**: `RunTrainResult`, with `MetadataId` (the run's metadata record), `ExternalId` (the
run's external id) and `Output` (the train's output, or `null` for a `Unit` train).

**Throws**:

- `TrainNotFoundException` or `AmbiguousTrainNameException`, as for `QueueAsync`.
- `TrainInputValidationException` when `inputJson` is over the size cap.
- `JsonException` when `inputJson` cannot be read, as for `QueueAsync`.
- `TrainAuthorizationException` when the train's `[TraxAuthorize]` requirements refuse the caller.
- `TrainAuthorizationNotConfiguredException`, under the same rule and trusted-scope exemption as
  `QueueAsync`.
- `InvalidOperationException` from the DI container when the train cannot be built, for example
  because a constructor dependency is not registered. The train is resolved before its metadata is
  written, so this leaves no `Pending` record.
- `NotSupportedException` when the host registered its own `ITrainBus` and it keeps the
  interface's default `RunByNameAsync`. It is thrown before any record is written.
- `TrainException` when the train itself fails (propagated from `ITrainBus`).

### What it does

1. Looks up the train by `trainName` through `ITrainDiscoveryService`.
2. Authorizes the caller against the train's requirements.
3. Reads `inputJson` into the train's `InputType`, as `QueueAsync` does.
4. Resolves the train found in step 1 by its canonical name, in a child DI scope. The train that
   runs is the one that was authorized, even when another train takes the same input type. A train
   that cannot be built throws here, before anything is written.
5. Creates a `Metadata` record with a generated external id and persists it as `Pending`.
6. Runs the train as that record, for the train's `OutputType`, invoked by reflection.
7. Returns the metadata id and the train's output. The output is read through a generic method
   closed over `OutputType`, so an output type that is not public (an `internal` record in the
   consumer's assembly) is returned like any other.

Steps 4 to 6 are the default `LocalRunExecutor` with the default `ITrainBus`. A host that registers
its own `ITrainBus` gets the record written first, then
`ITrainBus.RunByNameAsync<TOut>(trainName, input, ct, metadata)` called with it. Anything that
throws after the record is written and before the train takes it over (a replaced bus that cannot
build the train, a cancellation in that gap) leaves the record `Failed` with the exception
recorded, or `Cancelled` for an `OperationCanceledException`, rather than `Pending`. A record the
train already took over is left alone, because the train records its own outcome. A remote
executor (`UseRemoteRun`, `UseLambdaRun`) takes over steps 4 to 7.

## PrepareAsync

Looks the train up by name, authorizes the current caller for it, and reads the caller's input into
the train's input type: steps 1 to 3 of `QueueAsync` and `RunAsync`, which call the same code.
Nothing is written. The scheduler's run operation uses it to accept and refuse exactly what a queue
or a run does.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `trainName` | `string` | required | The train's name, matched by canonical name, then friendly name |
| `inputJson` | `string?` | required | JSON of the train's `InputType`, read as `QueueAsync` reads it, with the `MaxInputJsonBytes` cap applied |
| `ct` | `CancellationToken` | `default` | Cancels the lookup |

**Returns**: `PreparedTrain`, with `Registration` (the train's
[`TrainRegistration`](/docs/sdk-reference/mediator-api/i-train-discovery-service#trainregistration))
and `Input` (an instance of `Registration.InputType`, never null). `PreparedTrain` has no public
constructor, so code holding one knows the authorization check ran.

**Throws**: the same as `RunAsync` up to step 3: `TrainNotFoundException`,
`AmbiguousTrainNameException`, an `UnauthorizedAccessException` such as `TrainAuthorizationException`,
`TrainInputValidationException`, `JsonException`, and `TrainAuthorizationNotConfiguredException`.
An implementation of `ITrainExecutionService` written before this method existed inherits a
default that throws `NotSupportedException` rather than skipping the check.

Authorization runs before the input is read, so a caller who may not use the train learns nothing
about its input from a parse error.

## Behavior

### TrainInputReader

Every member reads input through
[TrainInputReader](/docs/sdk-reference/mediator-api/train-input-reader), so a host or package that
reads train input on a path of its own accepts and refuses the same JSON by calling it.

### Registered services

`AddMediator` registers `TrainExecutionService` as the scoped `ITrainExecutionService`, and the
`LocalRunExecutor` as the scoped `IRunExecutor` that `RunAsync` runs through.
[`UseRemoteRun`](/docs/sdk-reference/scheduler-api/use-remote-run) and
[`UseLambdaRun`](/docs/sdk-reference/scheduler-api/use-lambda-run) replace the executor.

## Examples

```csharp compile
[ApiController]
public class OrderController(ITrainExecutionService execution) : ControllerBase
{
    [HttpPost("orders/queue")]
    public async Task<IActionResult> QueueOrder([FromBody] JsonElement input, CancellationToken ct)
    {
        var result = await execution.QueueAsync(
            "MyApp.Trains.IProcessOrderTrain",
            input.GetRawText(),
            priority: 5,
            ct: ct);

        return Accepted(new { result.WorkQueueId, result.ExternalId });
    }

    [HttpPost("orders/run")]
    public async Task<IActionResult> RunOrder([FromBody] JsonElement input, CancellationToken ct)
    {
        var result = await execution.RunAsync(
            "MyApp.Trains.IProcessOrderTrain",
            input.GetRawText(),
            ct);

        return Ok(new { result.MetadataId, result.Output });
    }
}
```

Checking the name against discovery first, to answer 404 without calling the service:

```csharp compile
[ApiController]
public class TrainController(ITrainDiscoveryService discovery, ITrainExecutionService execution)
    : ControllerBase
{
    [HttpPost("trains/{trainName}/run")]
    public async Task<IActionResult> RunByName(
        string trainName,
        [FromBody] JsonElement input,
        CancellationToken ct)
    {
        var match = discovery.DiscoverTrains().FirstOrDefault(t => t.ServiceType.FullName == trainName);
        if (match is null)
            return NotFound();

        var result = await execution.RunAsync(trainName, input.GetRawText(), ct);
        return Ok(new { result.MetadataId });
    }
}
```

## See also

- [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus), the in-process bus `RunAsync` runs through
- [TrainInputReader](/docs/sdk-reference/mediator-api/train-input-reader)
- [Mediator exceptions](/docs/sdk-reference/mediator-api/exceptions)
- [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting)
- [OnQueue](/docs/mediator/enqueue-hooks)
