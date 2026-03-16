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
LoadMetadataJunction → ValidateMetadataStateJunction → RunScheduledTrainJunction →
                                             UpdateManifestSuccessJunction → SaveDatabaseChangesJunction
```

## Input

```csharp
public record RunJobRequest(long MetadataId, object? Input = null);
```

The `MetadataId` points to the `Metadata` row created by the [JobDispatcher](job-dispatcher.md). The `Input` is the deserialized train input passed through from the work queue.

## Junctions

### LoadMetadataJunction

Loads the `Metadata` record by ID, eagerly including its `Manifest` navigation (needed later by `UpdateManifestSuccessJunction`). If the input includes a non-null `Input` object, it wraps it in a `ResolvedTrainInput` for type-safe routing through Trax.Core's memory system.

### ValidateMetadataStateJunction

Checks that the loaded metadata is in `TrainState.Pending`. If it's already `InProgress`, `Completed`, or `Failed`, the junction throws. This guards against duplicate execution—if the job submitter retries a job that already started, this junction catches it.

### RunScheduledTrainJunction

Resolves the target train via `ITrainBus` using the deserialized input and invokes it. The train name stored in the metadata record is the canonical interface name (set via `CanonicalName` during DI registration), which `ITrainBus` uses for resolution. This is where your train's `RunInternal` method gets called. The train runs as a nested train under the JobRunner's own metadata, maintaining the parent-child relationship in the metadata tree.

### UpdateManifestSuccessJunction

If the train completed successfully and the metadata has an associated manifest, updates `Manifest.LastSuccessfulRun` to `DateTime.UtcNow`. This timestamp is what drives [dependent train](../dependent-trains.md) evaluation—downstream manifests won't fire until this value advances past their own `LastSuccessfulRun`.

If there's no manifest (e.g., an ad-hoc execution), this junction is a no-op.

### SaveDatabaseChangesJunction

Persists all pending database changes—primarily the `LastSuccessfulRun` update. This is a separate junction rather than being folded into `UpdateManifestSuccessJunction` so that the save happens as its own observable junction in the chain, with its own timing in junction metadata.

## Concurrency Model: Upstream Guarantee + State Guard

The JobRunner does not use any database-level locking of its own. Its safety relies on two mechanisms:

### Upstream Single-Dispatch Guarantee

The [JobDispatcher](job-dispatcher.md) uses `FOR UPDATE SKIP LOCKED` to atomically claim each WorkQueue entry before creating its Metadata record. This guarantees that for any given WorkQueue entry, exactly one Metadata record is created and exactly one background task is enqueued. The JobRunner inherits this guarantee — it is only invoked once per Metadata ID.

### State Validation Guard

`ValidateMetadataStateJunction` acts as a defense-in-depth check. It throws a `TrainException` if the metadata is in any state other than `Pending`. This catches edge cases where the job submitter might retry a job that has already started (e.g., after a visibility timeout). Once the `TrainBus` transitions the metadata to `InProgress`, any duplicate invocation will be rejected.

This is an **optimistic** guard — it reads the state without acquiring a lock. In the theoretical scenario where two workers execute the same Metadata ID simultaneously (which the JobDispatcher prevents), both could read `Pending` before either transitions to `InProgress`. This is acceptable because the upstream guarantee makes this scenario unreachable in practice.

### No Wrapping Transaction

The train does not wrap its junctions in an explicit transaction. `LoadMetadataJunction` loads the Metadata and its Manifest as **tracked EF Core entities** (not `AsNoTracking`), so `UpdateManifestSuccessJunction` can mutate `Manifest.LastSuccessfulRun` in memory and `SaveDatabaseChangesJunction` persists the change at the end. If the train fails before `SaveDatabaseChangesJunction`, `LastSuccessfulRun` is not updated — which is the correct behavior, since a failed execution should not advance the dependent train chain.

See [Multi-Server Concurrency](../concurrency.md) for the full cross-service concurrency model.

## Registration

All internal scheduler trains (`ManifestManagerTrain`, `JobDispatcherTrain`, `JobRunnerTrain`, `MetadataCleanupTrain`) are registered automatically by `AddScheduler()`, `AddTraxWorker()`, or `AddTraxJobRunner()`. Local workers (the implicit default when `UsePostgres()` is configured) register `JobRunnerTrain` as a scoped train route so that local workers can resolve and execute it. You do **not** need to include the `Trax.Scheduler` assembly in `AddMediator()` — only pass your own train assemblies.
