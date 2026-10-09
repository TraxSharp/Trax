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
    Task<OperationResult> ResumeExecutionAsync(long metadataId, string? from, CancellationToken ct);
    Task<OperationResult> CancelExecutionsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> CancelWorkQueueEntriesAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> SetManifestsEnabledAsync(IReadOnlyCollection<long> ids, bool enabled, CancellationToken ct);
    Task<OperationResult> SetManifestsReplayDecisionsOnRetryAsync(IReadOnlyCollection<long> ids, bool replay, CancellationToken ct);
    Task<OperationResult> SetManifestGroupsEnabledAsync(IReadOnlyCollection<long> ids, bool enabled, CancellationToken ct);
    Task<OperationResult> SetAllManifestGroupsEnabledAsync(bool enabled, CancellationToken ct);
    Task<TriggerManifestResult> TriggerManifestAsync(string externalId, TimeSpan? delay, bool askAfresh, CancellationToken ct);
    Task<BatchTriggerResult> TriggerManifestsAsync(IReadOnlyCollection<long> manifestIds, bool askAfresh, CancellationToken ct);
    Task<BatchTriggerResult> TriggerManifestGroupsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct);
    Task<OperationResult> CancelManifestGroupsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct);
    Task<ManifestExecutionStats> GetManifestExecutionStatsAsync(long manifestId, CancellationToken ct);
    Task<IReadOnlyList<ManifestGroupExecutionStats>> GetManifestGroupExecutionStatsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct);
    Task<LogPage> GetLogsAsync(LogQuery query, CancellationToken ct);
    Task<int> CountLogsAsync(LogQuery query, CancellationToken ct);
    Task<LogCount> CountLogsCappedAsync(LogQuery query, CancellationToken ct);
    Task<RecordedDecisionPage> GetRecordedDecisionsAsync(long metadataId, long? afterId, int take, CancellationToken ct);
    Task<WorkQueueEntryDetail?> GetWorkQueueEntryDetailAsync(long id, CancellationToken ct);
    Task<MachineInstancePage> GetMachineInstancesAsync(MachineInstanceQuery query, CancellationToken ct);
    Task<MachineInstanceTotal> CountMachineInstancesAsync(MachineInstanceQuery query, CancellationToken ct);
    Task<MachineInstanceRecord?> GetMachineInstanceAsync(MachineInstanceKey key, CancellationToken ct);
    Task<IReadOnlyList<MachineInstanceStateCount>> GetMachineInstanceStateCountsAsync(string? machine, CancellationToken ct);
    Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct);
    Task<OperationResult> UpdateManifestAsync(long id, ManifestUpdate update, CancellationToken ct);
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

### When nothing can dispatch a queued run

On a host whose store is in memory (EF Core's InMemory provider, with no database provider registered) the job dispatcher never runs, and no other process can reach the store, so a queued entry would never start. Every method here that would queue a run (`QueueTrainAsync`, `RequeueExecutionAsync`, `TriggerManifestAsync`, `TriggerManifestsAsync`, `TriggerManifestGroupsAsync`) refuses with `OperationsService.NoDispatcherMessage` and writes nothing. `QueueTrainAsync` and `RequeueExecutionAsync` first authorize the caller for the train as the mediator would, so a caller the train refuses gets the authorization failure, not this message. `ITraxScheduler`'s dead-letter requeues, which the dashboard and the API call directly, are refused the same way. `RunTrainAsync` is unaffected. A relational store is never refused, so an API-only host on PostgreSQL whose scheduler runs elsewhere keeps queueing. `ITraxScheduler.TriggerAsync`, called from code, is not refused. See [scheduler ADR 0019](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md).

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
plain form is over the stored-input cap (`StoredInputGrowthFactor` times `MaxInputJsonBytes`, since
the saved form is larger than what the caller sent, and PostgreSQL's `jsonb` renders it back with a
space after every `:` and `,`) gets the generic size message. The plain form is then handed over
written compactly, so `jsonb`'s spacing does not count against the cap. It is still the run's input
as the train saw it, every member written, defaults included, so it can be larger than the JSON the
caller first sent: an input accepted close to `MaxInputJsonBytes` that left members out can be over
it on requeue, and is refused with the generic size message. Queue it again with the original JSON,
or raise `MaxInputJsonBytes`. The enqueue then goes through the
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

A requeue always runs the chain again from the top, whatever checkpoints the run stored. To carry
on from a checkpoint instead, resume it.

### ResumeExecutionAsync

```csharp
Task<OperationResult> ResumeExecutionAsync(long metadataId, string? from, CancellationToken ct);
```

| Parameter | Type | Description |
|---|---|---|
| `metadataId` | `long` | The failed or cancelled run (metadata row) to resume |
| `from` | `string?` | The node id of the step to resume at, as the run graph names it, or `null` for after the run's latest checkpoint |

Queues a run that resumes a run from a
[checkpoint](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md):
it skips every step before the resume point and starts from the state the checkpoint stored. It is
a requeue in every check but where the run starts. It refuses, with a failed result and nothing
queued, when:

- no run has the id (`Execution {id} not found.`);
- the run is not `Failed` or `Cancelled` (`Execution {id} is {state}; only a failed or cancelled run can be resumed.`);
- a state machine's invoking state queued it, which only the machine retries, by entering the state again (`Execution {id} was started by a step of the state machine '{machine}', ... so it cannot be resumed. ...`);
- its saved input cannot be re-queued, for the reasons `RequeueExecutionAsync` gives;
- a resume of it is already queued (`A resume of execution {id} is already queued ...`);
- its train is no longer registered, or its chain cannot be read on this host;
- the resume check refuses: the run stored no checkpoint before `from`, a step after the point needs
  a value nothing restores, or the stored checkpoint no longer matches the running chain or the
  state's shape. The check's own reason is the message, unchanged.

Otherwise it enqueues through the mediator exactly as `RequeueExecutionAsync` does, with the run's
saved input, so the train's `[TraxAuthorize]`, its `OnQueue` hook and its subject key apply, and an
authorization failure propagates. The entry names the run in `ResumeFrom` and the step in
`ResumeAt`, and, when the run has decisions to replay, in `ReplayDecisionsOf` as a requeue would, so
a question asked after the resume point takes the track the run took. On success `Id` is the new
work queue entry. The new run checks the resume again when it starts, and runs from the top, with a
warning in its log, if it can no longer be trusted.

A run has at most one queued resume. When a manifest's retry, which resumes after the failed run's
latest checkpoint on its own (see
[Retries resume from a checkpoint](/docs/scheduler/dead-letters-and-cleanup#retries-resume-from-a-checkpoint)),
or another operator queues one between this call's checks and its insert, the database refuses the
second, and this call returns the same refusal.

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
| `CancelExecutionsAsync(ids, ct)` | Each run still `Pending` or `InProgress` gets `CancellationRequested`, and one running on this host is also cancelled at once through `ICancellationRegistry`. Terminal and unknown runs are skipped, and so is a run a step of a user's state-machine draft started, which an operator may not cancel: the message counts those and ends with `OperationsService.UserOwnedRunCancelRefusal`, and a single such id is refused with it. A `Pending` run is recorded `Cancelled` and never run when the job runner picks it up, whatever junction providers the host registers. An `InProgress` run observes the flag at its next junction boundary, when the host uses the junction progress provider. | `Execution`, when at least one run was flagged |
| `CancelWorkQueueEntriesAsync(ids, ct)` | Entries still `Queued` become `Cancelled`, in one statement, so an entry the dispatcher claims meanwhile keeps its status. An entry a user's draft queued is skipped and counted, as `CancelExecutionsAsync` skips its run, and `CancelWorkQueueEntryAsync` refuses one | `WorkQueue` |
| `SetManifestsEnabledAsync(ids, enabled, ct)` | Manifests whose `IsEnabled` differs | `Manifest` |
| `SetManifestsReplayDecisionsOnRetryAsync(ids, replay, ct)` | Manifests whose `ReplayDecisionsOnRetry` differs. Turning it off also clears the replay link of each manifest's queued entry, in the same transaction as the flag, so a retry waiting out its backoff asks afresh; the message then adds `{n} queued retry(s) no longer replay a failed run's decisions.` See [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions). | `Manifest`, when any changed; `WorkQueue`, when a link was cleared |
| `SetManifestGroupsEnabledAsync(ids, enabled, ct)` | Groups whose `IsEnabled` differs, with `UpdatedAt` bumped | `ManifestGroup` |
| `SetAllManifestGroupsEnabledAsync(enabled, ct)` | Every group whose `IsEnabled` differs; a separate method so that "all" is never what an empty list means | `ManifestGroup` |

| `CancelManifestGroupsAsync(groupIds, ct)` | Every `Pending` or `InProgress` run of every manifest in the given groups, by the rule `CancelExecutionsAsync` applies, flagged in one update. `Count` is the number of runs flagged; the message also says how many of the ids named a group. | `Execution`, when at least one run was flagged |

`ITraxScheduler.CancelAsync` and `CancelGroupAsync` cancel a manifest's or a group's runs by the same rule as `CancelExecutionsAsync`, and signal `Execution` the same way.

### Triggering one manifest

```csharp
Task<TriggerManifestResult> TriggerManifestAsync(string externalId, TimeSpan? delay, bool askAfresh, CancellationToken ct);

public record TriggerManifestResult(bool Success, string Message, ManifestTriggerResult? Trigger)
{
    public bool StillReplaying { get; init; }
}
```

Triggers the manifest with this external id as [`ITraxScheduler.TriggerAsync`](/docs/sdk-reference/scheduler-api/manifest-management#triggerasync) does, and says what the trigger did. The dashboard's **Run Now** and the API's `triggerManifest` and `triggerManifestDelayed` all call it. `delay` of `null` (or zero or less) means now, and a delay that would put the run past the latest time a `DateTime` holds (such as `TimeSpan.MaxValue`) is a failed result that queues nothing; an existing entry due sooner keeps its own time. `Message` says which happened: a new run queued, the manifest's queued run brought forward, one already due released as the trigger, or one the dispatcher claimed as the trigger reached it. `Trigger` carries the detail (`WorkQueueId`, `Created`, `MovedForward`, `ScheduledAt`, `AlreadyDispatched`, `ReplayDecisionsOf`). With `askAfresh: true` a released retry asks its deciders afresh; when the dispatcher claimed it first, the trigger still succeeds and `StillReplaying` is true. An unknown external id, or a host where nothing dispatches the queue, is a failed result with `Trigger` null and nothing changed.

### Updating a manifest

```csharp
Task<OperationResult> UpdateManifestAsync(long id, ManifestUpdate update, CancellationToken ct);

public record ManifestUpdate(
    bool? IsEnabled = null, int? MaxRetries = null, int? Priority = null, int? TimeoutSeconds = null,
    bool ClearTimeout = false, ScheduleType? ScheduleType = null, string? CronExpression = null,
    int? IntervalSeconds = null);
```

Patches one manifest; a `null` field is left as it is. Every check runs before a field is written, so a refused patch changes nothing: a negative `MaxRetries`, a `Priority` outside the work queue's range (0 to 31), a `TimeoutSeconds` or `IntervalSeconds` of 0 or less, a switch to `Cron` without an expression or to `Interval` without an interval, a switch to `Once`, `Dependent` or `DormantDependent` (they need a time or a parent the patch cannot give), and a cron expression the scheduler cannot use: not 5 or 6 fields, a field out of range (`99 * * * *`), or one that never fires (`0 0 30 2 *`). The schedule is checked only when the patch changes it, so a manifest already holding a bad schedule can still be disabled. Success signals `ChangeDomain.Manifest` and returns the manifest's id. The API's `updateManifest` calls it.

### Batch triggers

```csharp
Task<BatchTriggerResult> TriggerManifestsAsync(IReadOnlyCollection<long> manifestIds, bool askAfresh, CancellationToken ct);
Task<BatchTriggerResult> TriggerManifestGroupsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct);

public record BatchTriggerResult(
    bool Success, int Matched, int Queued, int AlreadyQueued, int TooLateToAskAfresh, int Skipped,
    string Message, IReadOnlyList<BatchItemNote> Notes);

public record BatchItemNote(long Id, string Message);
```

`TriggerManifestsAsync` triggers each manifest, by its database id, exactly as
[`ITraxScheduler.TriggerAsync`](/docs/sdk-reference/scheduler-api/manifest-management#triggerasync) triggers one by
external id: a new work queue entry marked as asked for by name, so it runs even while the manifest
is disabled, or, when the manifest already has a queued entry, that entry released as the triggered
run and brought forward to now. `TriggerManifestGroupsAsync` triggers the members
`ITraxScheduler.TriggerGroupAsync` would: the enabled manifests of each group that run on their own
schedule, leaving out Dependent and DormantDependent ones. Both take the same limits as the other
batches (an empty list or more than 1000 ids is a failed result with nothing triggered), trigger
the manifests one at a time in the order given, each with its own save, and signal `WorkQueue` once.

| Field | Meaning |
|---|---|
| `Success` | False only for a batch refused as given, or on a host where nothing dispatches the queue; every count is then zero |
| `Matched` | The ids that named a manifest, or a group, that exists |
| `Queued` | Manifests a new entry was queued for |
| `AlreadyQueued` | Manifests that already had a queued entry, which became the triggered run; also one the dispatcher had already claimed, which is running |
| `TooLateToAskAfresh` | Manifests triggered with `askAfresh: true` whose queued retry the dispatcher claimed first, so the run replays the failed run's decisions anyway; each has a note naming that run. Always zero for a group trigger |
| `Skipped` | Ids that named no manifest or group; each has a note |
| `Message` | One line for an operator, such as `2 queued, 1 already queued (that entry now runs as the trigger) across 3 of 3 manifest(s).` |
| `Notes` | One line per id that did not get what the trigger asked for |

With `askAfresh: true` a released retry that would replay a failed run's decisions asks its
deciders afresh instead (see
[Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)). A
database failure part way is thrown, not reported; the manifests triggered before it stay
triggered, and sending the same batch again is safe, since a manifest holds at most one queued
entry.

On a relational provider each batch is one `UPDATE` with its state test in it, so a row another writer changes meanwhile keeps its new state. The InMemory provider has no set-based update, so there the rows are loaded, changed and saved, with the same result and count ([scheduler ADR 0007](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0007-the-operations-surface-runs-on-inmemory.md)).

## Read models

The numbers behind a manifest's detail cards, the manifest groups list, the logs pages, a
run's recorded decisions, a work queue entry's page and the State Machines pages.

| Method | Returns |
|--------|---------|
| `GetManifestExecutionStatsAsync(manifestId, ct)` | `ManifestExecutionStats`: run counts by state, the latest start of any run and the latest end of a completed run. Zeros and nulls for a manifest with no runs. |
| `GetManifestGroupExecutionStatsAsync(groupIds, ct)` | One `ManifestGroupExecutionStats` per distinct id, in the order given: manifest count, run counts and the latest run. Zeros for a group with no manifests or runs; an empty list for no ids; more than 1000 ids throws `ArgumentOutOfRangeException`. |
| `GetLogsAsync(LogQuery, ct)` | A `LogPage` in the query's `Order`, newest first by default, filtered as below. `AfterId` pages by keyset in that order (older entries newest first, newer ones oldest first) and ignores `Skip`; prefer it, since an offset's cost grows with its size. `Take` is clamped to 1 through `OperationsService.MaxPageSize` (500). `NextCursor` is the last entry's id. |
| `CountLogsAsync(LogQuery, ct)` | The exact number of entries matching the filter, its text filters included; the order, cursor and paging fields are ignored. |
| `CountLogsCappedAsync(LogQuery, ct)` | A `LogCount(int Count, bool Capped)` for a pager. Without a text filter, the exact count `CountLogsAsync` gives, never capped. With `MessageContains` or `CategoryContains`, counted only up to `OperationsService.LogCountCap` (10,000): more matches than that give `Count = 10000, Capped = true`, to show as "10,000+"; exactly 10,000 is exact. The same on Postgres, Sqlite and InMemory. A custom implementation that does not override it returns the exact count, uncapped. |
| `GetWorkQueueEntryDetailAsync(id, ct)` | A `WorkQueueEntryDetail`, or `null` when no entry has the id: the entry's fields, its input masked as [below](#masking-a-stored-input), and for a queued entry with a subject, what it waits on. `SubjectHeldBy` is the dispatched entry for the same subject whose run is still `Pending` or `InProgress`; when nothing holds the subject, `SubjectQueuedBehind` is the confirmed queued sibling dispatch would offer first, by the filters dispatch applies (due, its manifest group enabled) and then priority and age. The dashboard's work queue entry page and the API's [`workQueue.detail`](/docs/sdk-reference/graphql-api/queries#detail) both read it. |
| `GetRecordedDecisionsAsync(metadataId, afterId, take, ct)` | A `RecordedDecisionPage` of the run's rows in `trax.decision`, in the order they were recorded, with answers withheld as described [below](#recorded-decisions). `afterId` is the previous page's `NextCursor`; `take` is clamped to 1 through 500. A run with no decisions gets an empty page. |

### LogQuery

```csharp
public record LogQuery(
    long? MetadataId = null, LogLevel? MinimumLevel = null, string? Category = null,
    long? AfterId = null, int Skip = 0, int Take = 25)
{
    public string? MessageContains { get; init; }
    public string? CategoryContains { get; init; }
    public LogOrder Order { get; init; } = LogOrder.NewestFirst;
}

public enum LogOrder { NewestFirst, OldestFirst }

public record LogCount(int Count, bool Capped);
```

| Filter | Matches |
|---|---|
| `MetadataId` | The entries of one run |
| `MinimumLevel` | Entries at this level or above |
| `Category` | Entries whose category is exactly this |
| `MessageContains` | Entries whose message contains this text, ignoring case. `%`, `_` and `\` match themselves. Null or empty matches everything |
| `CategoryContains` | Entries whose category contains this text, matched the same way |

Case is folded by the database: on Postgres by its collation, on Sqlite for ASCII letters only.
On Postgres a trigram index over the lowered message and category
([migration 063](/docs/migration-guides/database-migrations#log-text-search-063)) finds a term of
three characters or more. A page with a text filter, read by cursor or from the start, first
reads the 10,000 entries nearest where it starts in order, which fills it when the term is common
there, and finds the rest of its matches through the index and sorts them, so a term common only
among old entries does not make a newest-first page walk every newer one. Over 3,000,000 log rows a
page for a term no row carries, or one in a million carry, took about 10 ms; one for a term most rows
carry about 20 ms; one for a term in about 110,000 entries, all of them old, about 45 ms (500 ms when
the page was read in id order alone); and one for a term about one row in a hundred carries about
110 ms. A page read by offset (`Skip`) is still read in id order. An exact count reads every entry the
term matches, so `CountLogsAsync` for a term most rows carry took about 325 ms there;
`CountLogsCappedAsync` stops at 10,001 matches and took 13 ms for the same term, and at most about
85 ms for any term measured. On Sqlite, and for a
term shorter than three characters, nothing indexes text inside a message: a text filter reads
entries in id order until it fills a page, so a rare term reads the whole table.

An exact count of a large, unfiltered log table is a full scan, and the scheduler has no provider-neutral way to estimate one, so a pager that only needs an approximate size should estimate it itself.

### Recorded decisions

`GetRecordedDecisionsAsync` reads the rows [`AddDecisionRecording`](/docs/sdk-reference/configuration/add-decision-recording)
writes, one `RecordedDecisionRecord` each, a page at a time in id order from
`ix_decision_metadata_id_id` ([migration 064](/docs/migration-guides/database-migrations#decision-page-index-064-sqlite-028)):

```csharp
public record RecordedDecisionRecord(
    long Id, long MetadataId, string? QuestionKey, int Occurrence, string? Kind, string? Question,
    string? Answer, string? Refused, bool IsRefused, string? Fingerprint, string? Model,
    string? Decider, bool Replayed, string? Shadows, string? Routes, string? StateHash,
    DateTime DecidedAt, bool AnswerWithheld, bool TrackWithheld);

public record RecordedDecisionPage(IReadOnlyList<RecordedDecisionRecord> Items, int Take, long? NextCursor);
```

`Question`, `Answer`, `Shadows` and `Routes` are the row's JSON as stored. The stored row keeps
every value, because a requeue replays from it, so the read applies the rules
[junction events](/docs/sdk-reference/configuration/add-junction-events) apply before anything
leaves the service:

| Case | Left out |
|---|---|
| The question is about a type marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive) (`AnswerWithheld`) | `Answer`, `Refused`, `Shadows` and `Routes`, since each states or gives away the answer. The key and the question stay, and `IsRefused` still says whether the run refused it |
| The run took a track on such an answer before this decision (`TrackWithheld`) | Everything but `Id`, `MetadataId`, `Occurrence`, `IsRefused`, `Replayed` and `DecidedAt`, since which question came next gives the track away. A page that starts after the track is withheld too |

Whether a key names a marked type is worked out from the key, as junction events work it out, so
a key two types share is withheld when either is marked.

The replay links are plain columns read with the run and the entry: `Metadata.ReplayDecisionsOf`,
`Metadata.ReplayAbandoned` and `WorkQueue.ReplayDecisionsOf`.

### State-machine instances

The operator's read-only view of `trax.snapshot_draft`: the dashboard's
[State Machines pages](/docs/dashboard#state-machines) and the API's
[`machineInstances`, `machineInstance` and `machineInstanceCounts`](/docs/sdk-reference/graphql-api/queries#machineinstances)
read through these four methods, and one instance's runs and its cancel through the two after them.

```csharp
public record MachineInstanceQuery(
    string? Machine = null, string? State = null, SnapshotOwnerKind? OwnerKind = null,
    int Skip = 0, int Take = 25);

public record MachineInstanceKey(string Machine, SnapshotOwnerKind OwnerKind, Guid Id, long? RowId = null);

public record MachineInstanceRecord(
    long RowId, string Machine, SnapshotOwnerKind OwnerKind, Guid Id, string State, int Version,
    DateTimeOffset? CreatedAt, DateTimeOffset UpdatedAt, bool HasLiveInvokedRun);

public record MachineInstancePage(IReadOnlyList<MachineInstanceRecord> Items, int Skip, int Take);
public record MachineInstanceTotal(int Count, bool Capped);
public record MachineInstanceStateCount(string Machine, string State, SnapshotOwnerKind OwnerKind, long Count);
```

| Method | Returns |
|--------|---------|
| `GetMachineInstancesAsync(query, ct)` | A page of instances matching the query's machine, state and owner kind (each optional), newest first by `UpdatedAt`, then by row. `Take` is clamped to 1 through 500, a negative `Skip` reads from the start, and a `Skip` above `MachineInstanceCountCap` (10,000) throws `ArgumentOutOfRangeException`, as the API's `machineInstances` refuses it. |
| `CountMachineInstancesAsync(query, ct)` | How many match, counted up to `OperationsService.MachineInstanceCountCap` (10,000): more give `Count = 10000, Capped = true`. |
| `GetMachineInstanceAsync(key, ct)` | One instance, or `null`. The owner kind is part of every lookup, so a user's draft never answers for a system instance under the same id. A user's draft also needs `RowId`, because several users can hold a draft under one id: without it the method throws `ArgumentException`. |
| `GetMachineInstanceStateCountsAsync(machine, ct)` | Exact counts by machine, state and owner kind, ordered by those three; `machine` narrows it to one machine. Kept for `MachineInstanceCountCacheDuration` (5 seconds) per host and per `machine`, with callers asking meanwhile sharing one read, so a count can be that old. |

A record never carries the snapshot's context or the owning user's key. The context is an untyped
JSON object, so nothing could mask its sensitive parts; an operator sees where an instance is and
when it got there, never what it holds. `HasLiveInvokedRun` says whether the instance holds an invoke
token, not what the token is. `CreatedAt` is null for a row written before migration 071 (Sqlite
033) added it. On Postgres a page under one machine and state reads
`ix_snapshot_draft_machine_state_updated` in order, and any other page reads
`ix_snapshot_draft_updated`; at two million instances every page measured took under 10 ms and the
counts about 120 ms.

Two more methods serve one instance's page and its one operator action, the dashboard's and the
API's alike (`machineInstance { invokedRuns }` and
[`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations#cancelmachineinstance)):

```csharp
public record MachineInstanceRun(
    long Id, string ExternalId, string TrainName, TrainState TrainState, DateTime StartTime,
    DateTime? EndTime, FailureClass FailureClass, bool CancellationRequested, bool IsLive);

public record MachineInstanceRuns(IReadOnlyList<MachineInstanceRun> Items, bool Capped, long? QueuedEntryId);

public enum MachineInstanceCancelOutcome
{
    Moved, RunCancelled, CancelRequested, // the run was cancelled or its cancel requested
    UserOwned, NotFound, NoLiveRun, RunEnded, // refused, nothing changed
}

public record MachineInstanceCancelResult(
    MachineInstanceCancelOutcome Outcome, string Message, string? State = null)
{
    public bool Success { get; }
}
```

| Method | Returns |
|--------|---------|
| `GetMachineInstanceRunsAsync(key, ct)` | The runs the instance invoked, newest first, at most `OperationsService.MachineInstanceRunCap` (50), with the one its state waits on marked `IsLive`; `QueuedEntryId` is that run's work queue entry while it is still queued. `null` when no row matches. A system instance lists every run linked to it (read through `ix_metadata_invoking_instance`); a user's draft lists only the run its own invoke token names, because a run does not record which user's draft queued it. Never a run's input or output. |
| `CancelMachineInstanceAsync(key, ct)` | Cancels a system-owned instance's live run: a still-queued entry is marked Cancelled by one conditional statement, and a dispatched run is flagged as `CancelExecutionsAsync` flags one. The dispatcher claims an entry and writes its run in one transaction, so the run either never starts or is cancelled. When this host registers the machine (`AddStateMachines`), a queued run's Cancelled outcome is applied in the call (`Moved`); otherwise a host that does applies it (`RunCancelled`). Every delivery is the one conditional update on the token, so the outcome is applied once. A dispatched run's outcome is applied when it ends (`CancelRequested`). Refused, with a typed outcome and changing nothing: `UserOwned`, `NotFound`, `NoLiveRun`, `RunEnded`. |

The messages are public members of `OperationsService`, and both surfaces show them unchanged:

| Outcome | Message |
|---|---|
| `Moved` | `MovedMessage(key, state)`: the queued run was cancelled and the instance moved to `state` in this call |
| `RunCancelled` | `RunCancelledMessage(key)`: the queued run was cancelled; a host that registers the machine moves it |
| `CancelRequested` | `CancelRequestedMessage(key, state)`: the dispatched run's cancel flag is set |
| `UserOwned` | `UserOwnedCancelRefusal` |
| `NotFound` | `InstanceNotFoundMessage(key)` |
| `NoLiveRun` | `NoLiveRunMessage(key, state)` |
| `RunEnded` | `RunEndedMessage(key, state)`: the run ended before the cancel reached it, and its outcome is on its way |

A requeue of a run a machine invoked is refused with `InvokedRunRequeueRefusal(metadataId, machine)`,
and an operator's cancel of a run a user's draft started with `UserOwnedRunCancelRefusal`.

### Masking a stored input

A work queue entry's input and a manifest's properties keep their
[`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields)
members in clear, because a run starts from them. `TransportInputRedaction.Redact(discovery, json,
inputTypeName)`, in the same namespace, is the one masking both surfaces apply to those copies:
`GetWorkQueueEntryDetailAsync` applies it to the entry's input, and the dashboard's dead letter
page and the API's `manifestDetail` to a manifest's properties. It reads the copy back as the input
type of a train registered on this host and writes it with every sensitive member, and every member
typed `object` or as a JSON node, as `{"_redacted": true}`. When no registered train takes that
type, or the copy does not read or write as it (the type's own code throwing included), the whole
value is `{"_redacted": true}`.

## Effects and log levels

The effects page and the server log levels are per-process settings rather than rows, so they
have services of their own, registered by `AddScheduler` beside this one:
[IEffectSettingsService](/docs/sdk-reference/scheduler-api/i-effect-settings-service) and
[ILogLevelService](/docs/sdk-reference/scheduler-api/i-log-level-service). So does requeue-all,
whose job state is this node's:
[IDeadLetterRequeueJobs](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs).

## Authorization

Neither method decides authorization itself: both leave it to the mediator, `QueueTrainAsync` and `RequeueExecutionAsync` through `QueueAsync`, and `RunTrainAsync` through `PrepareAsync`. An `ITrainAuthorizationService` decides when one is registered (Trax.Api registers one). Without one, a call inside a trusted scope passes, and a `[TraxAuthorize]` train is refused unless the host called `AllowMissingAuthorizationService()`. The dashboard calls both inside the `"dashboard"` trusted scope, because it is gated as a whole by its host (see [Authorization: The Operations Surface](/docs/authorization#the-operations-surface)).

## Implementing it yourself

`RunTrainAsync` and every method added after it have a default implementation that throws `NotSupportedException`, so an implementation written before they were added still compiles. `OperationsService` needs the constructor that takes an `IServiceProvider`, which dependency injection picks, to resolve a job submitter; built with the older constructor, its `RunTrainAsync` throws `InvalidOperationException`. Its `RunTrainAsync` also needs an `ITrainExecutionService` that implements `PrepareAsync`, as the mediator's own does; one written before that method existed throws `NotSupportedException` rather than skip authorization.

## Package

```
dotnet add package Trax.Scheduler
```
