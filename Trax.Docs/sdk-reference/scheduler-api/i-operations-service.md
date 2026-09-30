---
layout: default
title: IOperationsService
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
    Task<OperationResult> CancelExecutionsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> CancelWorkQueueEntriesAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<OperationResult> SetManifestsEnabledAsync(IReadOnlyCollection<long> ids, bool enabled, CancellationToken ct);
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
| `QueueTrainAsync(QueueTrainInput(TrainName, InputJson, Priority, ScheduledAt), ct)` | Enqueues through `ITrainExecutionService.QueueAsync`, so the train's `[TraxAuthorize]` requirements, its `OnQueue` hook and its subject key apply. The entry waits for dispatch like any other. | the work queue entry |
| `RunTrainAsync(RunTrainInput(TrainName, InputJson), ct)` | Writes a `Pending` run and submits it at once to the job submitter the train is routed to, the same routing the job dispatcher uses (`ForTrain<T>()`, then `[TraxRemote]`, then the default submitter). Nothing goes through the work queue. When the submit throws, the exception is thrown to the caller and the run is recorded `Failed`, but only while it is still `Pending`: a runner that already started it (and then answered with an error, or outlasted the HTTP timeout) keeps its own record. | the run's metadata row |

Both look the train up by its interface `FullName` and hand the input to the mediator: `QueueTrainAsync` through `ITrainExecutionService.QueueAsync`, `RunTrainAsync` through `ITrainExecutionService.PrepareAsync`, which authorizes the caller and reads the input without writing anything. Either way `InputJson` is read by `TrainInputReader`: property names matched whatever their case (`customerId`, `CustomerId` and `CUSTOMERID` all fill the same property), a property given twice in any casing refused as invalid input rather than resolved to its last value, JSON reference metadata (`$id`, `$ref`, `$values`) not honoured, so the input is exactly the tree the caller wrote, the mediator's input size cap, and a blank input read as `{}`, which the input type must be buildable from.

The form a queued input is stored in, and the form a run's submitter writes for its worker, is indented and writes every member, so it is larger than the caller's JSON. Both are held to `TrainInputReader.StoredInputGrowthFactor` (4) times `MaxInputJsonBytes`, measured before anything is written or submitted.

A run is a deliberate bypass of the work queue. It skips dispatch priority, group `MaxActiveJobs`, and the subject lock, so it can run while another run for the same subject is in progress (see [QueueSubjectKey](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing)). Use `QueueTrainAsync` when that matters.

## Failures

Both methods return a failed `OperationResult` with a `Message` for an answer the caller can act on, and throw for a fault on the server's side.

| Outcome | `QueueTrainAsync` | `RunTrainAsync` |
|---------|-------------------|-----------------|
| Blank `TrainName`, unknown train | failed result | failed result |
| Invalid or `null` `InputJson`, a property given twice, or JSON reference metadata | failed result (`Invalid InputJson: ...`) | failed result, same message; no run is written |
| `InputJson` over `MaxInputJsonBytes`, or a stored form over its cap | failed result, a generic message | failed result, same message; no run is written |
| The train's `OnQueue` or `QueueSubjectKey` refused | failed result (`The enqueue was refused: ...`) | not applicable: a run has neither |
| The caller may not run the train | throws `UnauthorizedAccessException` | throws `UnauthorizedAccessException`, before the input is read |
| `[TraxAuthorize]` train, no enforcer, not trusted | logged and thrown (`TrainAuthorizationNotConfiguredException`) | throws `TrainAuthorizationNotConfiguredException` |
| Database or network failure | logged and thrown | logged and thrown |
| The job submitter failed | not applicable | the run is marked `Failed` with the submitter's exception, then it is logged and thrown |
| `ct` cancelled | throws `OperationCanceledException` | throws `OperationCanceledException`; a run cancelled before its submit completed is marked `Failed` |

A thrown failure reaches Trax.Api's error filter, which masks any type it does not know as `Unexpected Execution Error`, so a connection string's host and port never reach a GraphQL client.

## Batch actions

The actions a list page applies to its selected rows. Each takes up to `OperationsService.MaxBatchSize` (1000) ids, ignores duplicates, writes only the rows that change, and returns `OperationResult(true, Count: N)` with the number changed, zero included. An empty list, or more ids than the limit, is a failed result and changes nothing.

| Method | What changes | Change signal |
|--------|--------------|---------------|
| `CancelExecutionsAsync(ids, ct)` | Each run still `Pending` or `InProgress` gets `CancellationRequested`, and one running on this host is also cancelled at once through `ICancellationRegistry`. Terminal and unknown runs are skipped. A run observes the flag at its next junction boundary, when the host uses the junction progress provider. | `Execution`, when at least one run was flagged |
| `CancelWorkQueueEntriesAsync(ids, ct)` | Entries still `Queued` become `Cancelled`, in one statement, so an entry the dispatcher claims meanwhile keeps its status | `WorkQueue` |
| `SetManifestsEnabledAsync(ids, enabled, ct)` | Manifests whose `IsEnabled` differs | `Manifest` |
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

Neither method decides authorization itself: both leave it to the mediator, `QueueTrainAsync` through `QueueAsync` and `RunTrainAsync` through `PrepareAsync`. An `ITrainAuthorizationService` decides when one is registered (Trax.Api registers one). Without one, a call inside a trusted scope passes, and a `[TraxAuthorize]` train is refused unless the host called `AllowMissingAuthorizationService()`. The dashboard calls both inside the `"dashboard"` trusted scope, because it is gated as a whole by its host (see [Authorization: The Operations Surface](/docs/authorization#the-operations-surface)).

## Implementing it yourself

`RunTrainAsync` and every method added after it have a default implementation that throws `NotSupportedException`, so an implementation written before they were added still compiles. `OperationsService` needs the constructor that takes an `IServiceProvider`, which dependency injection picks, to resolve a job submitter; built with the older constructor, its `RunTrainAsync` throws `InvalidOperationException`. Its `RunTrainAsync` also needs an `ITrainExecutionService` that implements `PrepareAsync`, as the mediator's own does; one written before that method existed throws `NotSupportedException` rather than skip authorization.

## Package

```
dotnet add package Trax.Scheduler
```
