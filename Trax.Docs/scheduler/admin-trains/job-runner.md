---
layout: default
title: JobRunner
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 3
---

# JobRunnerTrain

The JobRunner is what actually runs your train. It executes on job submitter workers and handles the bookkeeping around each execution: loading the metadata, validating state, invoking the train, and recording success.

## Chain

```
LoadMetadataJunction → RunScheduledTrainJunction
```

## Input

```csharp
public record RunJobRequest(long MetadataId, object? Input = null);
```

The `MetadataId` points to the `Metadata` row created by the [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher). The `Input` is the deserialized train input passed through from the work queue.

## Junctions

### LoadMetadataJunction

Loads the `Metadata` record by ID, eagerly including its `Manifest` navigation (needed later by `RunScheduledTrainJunction` to record the success), and requires a non-null `Input`.

It then resolves the train the row names. The row's `Name` is the train's canonical name, its interface's `FullName`; a row written by an older version may carry the interface's short name or the class's name, which is accepted when exactly one registered train taking the input goes by it. The train must be registered and must take the input given, and it may not be one of the scheduler's own trains, which the scheduler runs in its own process. Otherwise the junction throws before anything touches the row, which stays `Pending`.

Two trains may take the same input type, so the train is found by the name on the row, never by the input's type. The input and the train's canonical name travel on in a `ResolvedTrainInput`, a wrapper for routing through Trax.Core's memory system.

### RunScheduledTrainJunction

If the loaded row is no longer `Pending`, another delivery of the same job already started it, and this one completes without running anything (see [Duplicate deliveries](#duplicate-deliveries)). Otherwise it runs the train `LoadMetadataJunction` resolved, by name, through [`ITrainBus.RunByNameAsync`](/docs/sdk-reference/mediator-api/train-bus), with the deserialized input. The input-keyed `ITrainBus.RunAsync` reaches only one train per input type, so it is not used here: the train that runs is always the one the row names. This is where your train's `Junctions()` declaration gets run. The train runs as the `Pending` metadata record the dispatcher created (the request's `MetadataId`), passed to `RunByNameAsync`, so its execution is recorded on that row. The JobRunner's own run is a separate record, and the two are not linked by `ParentId`.

Once the train has returned, the same junction records the success on the manifest: it sets `Manifest.LastSuccessfulRun` to `DateTime.UtcNow`, computes `NextScheduledRun`, and disables a `ScheduleType.Once` manifest. `LastSuccessfulRun` is what drives [dependent train](/docs/scheduler/dependent-trains) evaluation: downstream manifests won't fire until this value advances past their own `LastSuccessfulRun`. If there's no manifest (e.g., an ad-hoc execution), this step is a no-op.

The update and its save run on an uncancellable token, inside this junction rather than as junctions of their own. A train checks its token before every junction, so a host shutdown that lands after your train completed would otherwise skip the update, leaving `LastSuccessfulRun` stale and a `Once` manifest enabled to run again. `Trax.Scheduler/docs/adr/0005` records why this is not split into separate junctions.

## Concurrency Model: One Dispatch, and a Claimed Start

### Upstream Single-Dispatch Guarantee

The [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher) uses `FOR UPDATE SKIP LOCKED` to atomically claim each WorkQueue entry before creating its Metadata record. For any given WorkQueue entry, exactly one Metadata record is created and one job is submitted.

### Duplicate deliveries

A submitted job can still reach a runner more than once: SQS delivers at least once, an HTTP or Lambda dispatch can be retried after the first attempt was accepted, and a local job can be claimed again. The run's `Pending` row decides which delivery runs it. Starting a train from a pre-created row claims the row in the store with one conditional write, which moves it to `InProgress` only while it is still `Pending`, so exactly one delivery wins, even when both loaded the row as `Pending`.

Every other delivery completes without running the train and records nothing: not on the run's row, not on the manifest, and not as a failure of its own JobRunner run. Whether it sees a row that is already `InProgress`, `Completed`, `Failed` or `Cancelled`, or loses the claim to a delivery that started a moment earlier, it logs that the run was already started and returns normally. Its transport therefore acknowledges it: the local worker deletes the job row, an SQS record is not returned to the queue, and a runner endpoint answers with success.

### No Wrapping Transaction

The train does not wrap its junctions in an explicit transaction. `LoadMetadataJunction` loads the Metadata and its Manifest as **tracked EF Core entities** (not `AsNoTracking`), so `RunScheduledTrainJunction` can mutate `Manifest.LastSuccessfulRun` in memory and save it once the train has returned. If the train fails, `LastSuccessfulRun` is not updated, which is the correct behavior, since a failed execution should not advance the dependent train chain.

See [Multi-Server Concurrency](/docs/scheduler/concurrency) for the full cross-service concurrency model.

## Registration

All internal scheduler trains (`ManifestManagerTrain`, `JobDispatcherTrain`, `JobRunnerTrain`, `MetadataCleanupTrain`, `DeadLetterCleanupTrain`) are registered automatically by `AddScheduler()`, `AddTraxWorker()`, or `AddTraxJobRunner()`. Local workers (the implicit default when `UsePostgres()` is configured) register `JobRunnerTrain` as a scoped train route so that local workers can resolve and execute it. You do **not** need to include the `Trax.Scheduler` assembly in `AddMediator()`, only pass your own train assemblies.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner)
