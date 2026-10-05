---
layout: default
title: IOperationsService
description: "Reference for IOperationsService, the operations the dashboard and the GraphQL operations namespace share: queueing, running, batch actions and read models."
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 14
---

# IOperationsService

The operations the dashboard and the GraphQL `operations` namespace share. Each action an operator can take has one method here, and both surfaces call it, so the dashboard and the API validate, authorize and report failures the same way. Registered as scoped by `AddScheduler(...)`. An API-only host registers it itself with `services.AddScoped<IOperationsService, OperationsService>()` (see [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)).

## Signature

```csharp
namespace Trax.Scheduler.Services.Operations;

public interface IOperationsService
{
    Task<OperationResult> QueueTrainAsync(QueueTrainInput input, CancellationToken ct);
    Task<OperationResult> RunTrainAsync(RunTrainInput input, CancellationToken ct);
    Task<OperationResult> RequeueExecutionAsync(long metadataId, CancellationToken ct);
    Task<OperationResult> RequeueExecutionAsync(long metadataId, bool askAfresh, CancellationToken ct);
    Task<OperationResult> CancelExecutionsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> CancelWorkQueueEntriesAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> SetManifestsEnabledAsync(IReadOnlyCollection<long> ids, bool enabled, CancellationToken ct);
    Task<OperationResult> SetManifestsReplayDecisionsOnRetryAsync(IReadOnlyCollection<long> ids, bool replay, CancellationToken ct);
    Task<OperationResult> SetManifestGroupsEnabledAsync(IReadOnlyCollection<long> ids, bool enabled, CancellationToken ct);
    Task<OperationResult> SetAllManifestGroupsEnabledAsync(bool enabled, CancellationToken ct);
    Task<ManifestExecutionStats> GetManifestExecutionStatsAsync(long manifestId, CancellationToken ct);
    Task<IReadOnlyList<ManifestGroupExecutionStats>> GetManifestGroupExecutionStatsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct);
    Task<LogPage> GetLogsAsync(LogQuery query, CancellationToken ct);
    Task<int> CountLogsAsync(LogQuery query, CancellationToken ct);
    Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct);
    Task<OperationResult> UpdateManifestGroupAsync(long id, UpdateManifestGroupInput input, CancellationToken ct);
    Task<OperationResult> UpdateSchedulerConfigAsync(UpdateSchedulerConfigInput input, CancellationToken ct);
    // plus the read operations: metrics, scheduler config, manifest group graphs
}

public record OperationResult(bool Success, long? Id = null, int? Count = null, string? Message = null);
```

## Queueing and running

| Method | What it does | `Id` on success |
|--------|--------------|-----------------|
| `QueueTrainAsync(QueueTrainInput(TrainName, InputJson, Priority, ScheduledAt), ct)` | Enqueues through `ITrainExecutionService.QueueAsync`, so the train's `[TraxAuthorize]` requirements, its `OnQueue` hook and its subject key apply. The entry waits for dispatch like any other. The queued run asks its deciders afresh: of the methods here, only [`RequeueExecutionAsync`](#requeueexecutionasync) queues a run that replays an earlier one's decisions. | the work queue entry |
| `RunTrainAsync(RunTrainInput(TrainName, InputJson), ct)` | Applies the per-record checks a queue applies (below), writes a `Pending` run and submits it at once to the job submitter the train is routed to, the same routing the job dispatcher uses (`ForTrain<T>()`, then `[TraxRemote]`, then the default submitter). Nothing goes through the work queue. Once the run's row is written the submit no longer takes `ct`, so a caller that goes away does not abort the submit or cancel the run; the submitter's own timeouts bound it. When the submit throws and the run is still `Pending`, no runner started it: the run is recorded `Failed` and the exception is thrown to the caller. When a runner already started it (a remote runner that answered with the train's error, a call that timed out while the run went on, or an in-process submitter that ran a failing train), the run owns its outcome: the call succeeds with the run's id and the message `Run {id} of {train} submitted; its outcome is pending on the run.`, and the run's row records how it ended. | the run's metadata row |

Both look the train up by its interface `FullName` and hand the input to the mediator: `QueueTrainAsync` through `ITrainExecutionService.QueueAsync`, `RunTrainAsync` through `ITrainExecutionService.PrepareAsync`, which authorizes the caller and reads the input without writing anything. Either way `InputJson` is read by `TrainInputReader`: property names matched whatever their case (`customerId`, `CustomerId` and `CUSTOMERID` all fill the same property), a property given twice in any casing refused as invalid input rather than resolved to its last value, JSON reference metadata (`$id`, `$ref`, `$values`) not honoured, so the input is exactly the tree the caller wrote, the mediator's input size cap, and a blank input read as `{}`, which the input type must be buildable from.

The form a queued input is stored in, and the form a run's submitter writes for its worker, is indented and writes every member, so it is larger than the caller's JSON. Both are held to `TrainInputReader.StoredInputGrowthFactor` (4) times `MaxInputJsonBytes`, measured before anything is written or submitted.

A run applies the per-record checks a queue applies. When the train overrides `OnQueue`, the hook runs on the run's input before the run's row is saved, as it runs for an enqueue: on an instance in a scope of its own, with `TrainInput` reading the input, with the `metadata.ExternalId` the run executes under, with writes on [`IEnqueueContextAccessor.Current`](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) saved together with the run's row, and within `MaxQueueHookDuration`. A hook that throws refuses the run and nothing is written.

A run is otherwise a deliberate bypass of the work queue. It skips dispatch priority, group `MaxActiveJobs`, and the subject lock. A train that overrides [`QueueSubjectKey`](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) serializes its work per subject through the queue, so it is run now only inside a trusted scope, such as the dashboard's; any other caller gets a failed result telling it to queue the train instead. Use `QueueTrainAsync` when a run must wait its turn.

### RequeueExecutionAsync

```csharp
Task<OperationResult> RequeueExecutionAsync(long metadataId, CancellationToken ct);
Task<OperationResult> RequeueExecutionAsync(long metadataId, bool askAfresh, CancellationToken ct);
```

| Parameter | Type | Description |
|---|---|---|
| `metadataId` | `long` | The run (metadata row) to re-queue |
| `askAfresh` | `bool` | When `true`, the new entry carries no replay link and the new run asks every question again; nothing else differs. The overload without it is `askAfresh: false`. |

Re-queues a run: queues a fresh run of the same train with the input the run recorded. The GraphQL
[`requeueExecution`](/docs/sdk-reference/graphql-api/mutations#requeueexecution) mutation and the
dashboard's **Re-queue** button both call it, so the two refuse the same runs with the same
messages and enqueue the same way. The dashboard calls it inside its `"dashboard"` trusted scope;
the API does not.

It reads the run by `metadataId` and refuses, with a failed result and nothing queued, when the
run does not exist (`Execution {id} not found.`), when its train is no longer registered
(`Train {name} is no longer registered, so execution {id} cannot be re-queued.`), or when its
saved input is not the input it ran with:
nothing was saved (inputs are saved only when `SaveTrainParameters()` is on), the parameter effect
saved a `_truncated`, `_unserializable` or `_disposed` placeholder in its place, or
`[TraxSensitive]` members were masked. Each of those would read back as default values.

The saved input is written with reference metadata (`$id`, `$values`, `$ref`) that the mediator
does not honour in an input it is handed, so it is first resolved with
[`TrainInputReader.ResolveSavedInput`](/docs/sdk-reference/mediator-api/train-execution#resolvesavedinput)
into the plain tree the run was given: lists come back as lists, and an object that appeared twice
comes back in full both times. A saved input whose metadata has no plain form is refused
(`Execution {id}'s saved input cannot be read back as the input it ran with: ...`), and one whose
plain form is over the input cap gets the generic size message. The enqueue then goes through the
same path as `QueueTrainAsync`, so the train's authorization, its `OnQueue` hook, its subject key
and the input cap apply, and a refusal or failure is reported as it is there, with one difference:
the caller supplied no JSON, so an input that no longer reads as the train's input type (the type
changed shape after the run) is refused as
`The saved input of run {id} no longer reads as {InputType.FullName}: ...`, not as
`Invalid InputJson`. Like an enqueue's parse error, that is given only once the caller is
authorized. On success `Id` is the new work queue entry.

When the run has decisions to replay (it recorded a decision, or was itself queued to replay
another run), the new entry names it in `ReplayDecisionsOf`, so the new run
[replays those decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions) and takes
the tracks the original took. A requeue of a requeue therefore replays too, following the chain
back to the answers the first run recorded. A run with nothing to replay is re-queued as an
ordinary enqueue. It asks
[`HasDecisionsToReplay`](/docs/sdk-reference/configuration/add-decision-recording#hasdecisionstoreplay).
It is the only caller-facing method that sets the link, always to the run being re-queued; the
scheduler sets it on a manifest's retry and dead-letter requeue from its own checks (see
[Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)).
Each replayed answer is still checked against the state it is asked about now and its age, so a
question whose state changed, or whose answer is older than `ReplayAnswersFor`, is asked afresh.

A run's answers are replayed once. When a queued entry (a manifest's retry, an earlier requeue) or
a run in any state already replays the run being re-queued, the requeue still queues it but asks
afresh, and the success message ends
`It asks its deciders afresh: the decisions of execution {id} are already replayed by another run or queued entry, and are replayed once.`
The same holds when another enqueue queues a replay of the run in the same instant: the database
allows one queued entry per replayed run, so the requeue that loses is queued without the link,
asks afresh, and says so in the same message. With `askAfresh: true` no link is set and the message
adds nothing.

When the run has decisions to replay and the registered `ITrainExecutionService` does
not implement the
[`QueueAsync` overload that takes `QueueTrainOptions`](/docs/sdk-reference/mediator-api/train-execution),
the mediator's `DecisionReplayNotSupportedException` is logged and thrown as a host
misconfiguration rather than reported as `The enqueue was refused.`

## Failures

Both methods return a failed `OperationResult` with a `Message` for an answer the caller can act on, and throw for a fault on the server's side.

| Outcome | `QueueTrainAsync` | `RunTrainAsync` |
|---------|-------------------|-----------------|
| Blank `TrainName`, unknown train | failed result | failed result |
| Invalid or `null` `InputJson`, a property given twice, or JSON reference metadata | failed result (`Invalid InputJson: ...`) | failed result, same message; no run is written |
| `InputJson` over `MaxInputJsonBytes`, or a stored form over its cap | failed result, a generic message | failed result, same message; no run is written |
| The train's `OnQueue` or `QueueSubjectKey` refused | failed result: `The enqueue was refused: {message}` for a plain `TrainException`, `QueuedWorkCancelledException` or `QueueHookTimeoutException`, otherwise the fixed `The enqueue was refused.` with the exception logged at Warning | the `OnQueue` hook refused: failed result under the same rule, as `The run was refused: {message}` or `The run was refused.`; no run is written |
| A train that overrides `QueueSubjectKey`, outside a trusted scope | not applicable | failed result telling the caller to queue it instead; no run is written |
| The caller may not run the train | throws `UnauthorizedAccessException` | throws `UnauthorizedAccessException`, before the input is read |
| `[TraxAuthorize]` train, no enforcer, not trusted | logged and thrown (`TrainAuthorizationNotConfiguredException`) | throws `TrainAuthorizationNotConfiguredException` |
| Database or network failure, including one inside the `OnQueue` hook | logged and thrown | logged and thrown |
| The job submitter failed before a runner started the run | not applicable | the run is marked `Failed` with the submitter's exception, then it is logged and thrown |
| The job submitter threw after a runner started the run | not applicable | success, with the run's id; the message says its outcome is pending on the run, and the run's row records it |
| `ct` cancelled | throws `OperationCanceledException` | throws `OperationCanceledException` before the run's row is written; after that `ct` is not passed to the submit, so it does not cancel the run |

A thrown failure reaches Trax.Api's error filter, which masks any type it does not know as `Unexpected Execution Error`, so a connection string's host and port never reach a GraphQL client.

## Batch actions

The actions a list page applies to its selected rows. Each takes up to `OperationsService.MaxBatchSize` (1000) ids, ignores duplicates, writes only the rows that change, and returns `OperationResult(true, Count: N)` with the number changed, zero included. An empty list, or more ids than the limit, is a failed result and changes nothing.

| Method | What changes | Change signal |
|--------|--------------|---------------|
| `CancelExecutionsAsync(ids, ct)` | Each run still `Pending` or `InProgress` gets `CancellationRequested`, and one running on this host is also cancelled at once through `ICancellationRegistry`. Terminal and unknown runs are skipped. A `Pending` run is recorded `Cancelled` and never run when the job runner picks it up, whatever junction providers the host registers. An `InProgress` run observes the flag at its next junction boundary, when the host uses the junction progress provider. | `Execution`, when at least one run was flagged |
| `CancelWorkQueueEntriesAsync(ids, ct)` | Entries still `Queued` become `Cancelled`, in one statement, so an entry the dispatcher claims meanwhile keeps its status | `WorkQueue` |
| `SetManifestsEnabledAsync(ids, enabled, ct)` | Manifests whose `IsEnabled` differs | `Manifest` |
| `SetManifestsReplayDecisionsOnRetryAsync(ids, replay, ct)` | Manifests whose `ReplayDecisionsOnRetry` differs. Turning it off also clears the replay link of each manifest's queued entry, in the same transaction as the flag, so a retry waiting out its backoff asks afresh; the message then adds `{n} queued retry(s) no longer replay a failed run's decisions.` See [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions). | `Manifest`, when any changed; `WorkQueue`, when a link was cleared |
| `SetManifestGroupsEnabledAsync(ids, enabled, ct)` | Groups whose `IsEnabled` differs, with `UpdatedAt` bumped | `ManifestGroup` |
| `SetAllManifestGroupsEnabledAsync(enabled, ct)` | Every group whose `IsEnabled` differs; a separate method so that "all" is never what an empty list means | `ManifestGroup` |

`ITraxScheduler.CancelAsync` and `CancelGroupAsync` cancel a manifest's or a group's runs by the same rule as `CancelExecutionsAsync`, and signal `Execution` the same way.

On a relational provider each batch is one `UPDATE` with its state test in it, so a row another writer changes meanwhile keeps its new state. The InMemory provider has no set-based update, so there the rows are loaded, changed and saved, with the same result and count ([scheduler ADR 0007](https://github.com/TraxSharp/Trax.Scheduler/blob/main/docs/adr/0007-the-operations-surface-runs-on-inmemory.md)).

## Read models

The numbers behind a manifest's detail cards, the manifest groups list and the logs page.

| Method | Returns |
|--------|---------|
| `GetManifestExecutionStatsAsync(manifestId, ct)` | `ManifestExecutionStats`: run counts by state, the latest start of any run and the latest end of a completed run. Zeros and nulls for a manifest with no runs. |
| `GetManifestGroupExecutionStatsAsync(groupIds, ct)` | One `ManifestGroupExecutionStats` per distinct id, in the order given: manifest count, run counts and the latest run. Zeros for a group with no manifests or runs; an empty list for no ids; more than 1000 ids throws `ArgumentOutOfRangeException`. |
| `GetLogsAsync(LogQuery, ct)` | A `LogPage`, newest first, filtered by `MetadataId`, `MinimumLevel` and exact `Category`. `AfterId` pages by keyset and ignores `Skip`; prefer it, since an offset's cost grows with its size. `Take` is clamped to 1 through `OperationsService.MaxPageSize` (500). `NextCursor` is the last entry's id. |
| `CountLogsAsync(LogQuery, ct)` | The exact number of entries matching the filter; the cursor and paging fields are ignored. |

An exact count of a large, unfiltered log table is a full scan, and the scheduler has no provider-neutral way to estimate one, so a pager that only needs an approximate size should estimate it itself.

## Authorization

Neither method decides authorization itself: both leave it to the mediator, `QueueTrainAsync` and `RequeueExecutionAsync` through `QueueAsync`, and `RunTrainAsync` through `PrepareAsync`. An `ITrainAuthorizationService` decides when one is registered (Trax.Api registers one). Without one, a call inside a trusted scope passes, and a `[TraxAuthorize]` train is refused unless the host called `AllowMissingAuthorizationService()`. The dashboard calls both inside the `"dashboard"` trusted scope, because it is gated as a whole by its host (see [Authorization: The Operations Surface](/docs/authorization#the-operations-surface)).

## Implementing it yourself

`RunTrainAsync` and every method added after it have a default implementation that throws `NotSupportedException`, so an implementation written before they were added still compiles. `OperationsService` needs the constructor that takes an `IServiceProvider`, which dependency injection picks, to resolve a job submitter; built with the older constructor, its `RunTrainAsync` throws `InvalidOperationException`. Its `RunTrainAsync` also needs an `ITrainExecutionService` that implements `PrepareAsync`, as the mediator's own does; one written before that method existed throws `NotSupportedException` rather than skip authorization.

## Package

```
dotnet add package Trax.Scheduler
```
