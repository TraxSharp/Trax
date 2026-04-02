---
layout: default
title: ManifestManager
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 1
---

# ManifestManagerTrain

The ManifestManager is the first half of each polling cycle. It figures out which manifests are due for execution and writes them to the work queue. It doesn't dispatch anything; that's the [JobDispatcher's](job-dispatcher.md) job.

## Chain

```
LoadManifestsJunction → CancelTimedOutJobsJunction → ReapStalePendingMetadataJunction → ReapStaleInProgressMetadataJunction → ReapFailedJobsJunction → DetermineJobsToQueueJunction → CreateWorkQueueEntriesJunction
```

## Junctions

### LoadManifestsJunction

Projects all enabled manifests into lightweight `ManifestDispatchView` records using a single database query with pre-computed aggregate flags (`FailedCount`, `HasAwaitingDeadLetter`, `HasQueuedWork`, `HasActiveExecution`, `HasSuccessfulMetadata`). These flags are computed via COUNT/EXISTS subqueries pushed into the database, keeping query cost O(manifests) regardless of how large the child tables (`Metadatas`, `DeadLetters`, `WorkQueues`) grow.

The projection uses `AsNoTracking()`, the results are read-only snapshots used for scheduling decisions only. No unbounded child collections are loaded into memory.

> **Scaling note:** LoadManifestsJunction loads all enabled manifests in a single query. For typical production deployments (up to 10K manifests), this is efficient with proper indexes. The junction cannot be paginated without breaking the reap junctions' ability to identify stale jobs across all manifests.

### CancelTimedOutJobsJunction

Finds InProgress metadata that has exceeded `DefaultJobTimeout` and requests cooperative cancellation. Sets `CancellationRequested = true` in the database (picked up by `CancellationCheckProvider` at the next junction boundary) and attempts same-server instant cancellation via the `CancellationRegistry`.

### ReapStalePendingMetadataJunction

Fails Pending metadata that has not been picked up within `StalePendingTimeout` (default: 20 minutes). Acts as a safety net for dispatch failures where the worker never started executing the job (e.g., remote worker unreachable, Lambda crashed after receiving the request).

### ReapStaleInProgressMetadataJunction

Fails InProgress metadata that has not completed within `StaleInProgressTimeout` (default: 60 minutes). Acts as a safety net for hard crashes. Lambda hard-kills, OOM events, or process crashes where the worker dies without reaching `FinishServiceTrain`. This timeout should be longer than `DefaultJobTimeout` to allow cooperative cancellation (via `CancelTimedOutJobsJunction`) to propagate before force-failing.

Newly-failed metadata from both stale reapers is visible to `ReapFailedJobsJunction` in the same ManifestManager cycle, enabling dead-lettering if retries are exhausted.

### ReapFailedJobsJunction

Scans loaded manifests for any whose failure count meets or exceeds `MaxRetries`. For each, it creates a `DeadLetter` record with status `AwaitingIntervention` and persists immediately.

A manifest is only reaped if it doesn't already have an unresolved dead letter. This prevents duplicate dead letters from accumulating when the same manifest fails across multiple polling cycles.

`FailedCount` only counts failures that occurred **after** the most recent dead letter resolution. When a dead letter is resolved (retried or acknowledged), the failure counter effectively resets, only new failures contribute toward the next `MaxRetries` threshold. This prevents retried manifests from being immediately re-dead-lettered due to historical failures that were already addressed.

The junction returns the list of newly created dead letters so `DetermineJobsToQueueJunction` can skip those manifests without re-querying the database.

### DetermineJobsToQueueJunction

The decision junction. It runs two passes over the loaded manifests:

**Pass 1: Time-based manifests** (Cron and Interval). For each, it checks whether the manifest is due using `SchedulingHelpers.ShouldRunNow()`, which dispatches to either cron parsing or interval arithmetic based on the schedule type.

**Pass 2: Dependent manifests**. For each manifest with `ScheduleType.Dependent`, it finds the parent in the loaded set and checks whether `parent.LastSuccessfulRun > dependent.LastSuccessfulRun`. Before comparing timestamps, the junction verifies that the parent has at least one `Completed` metadata record (`HasSuccessfulMetadata`). If the parent has a `LastSuccessfulRun` timestamp but no successful metadata to back it up (e.g., metadata was truncated or pruned), the timestamp is considered stale and the dependent is not queued. See [Dependent Trains](../dependent-trains.md).

Manifests with `ScheduleType.DormantDependent` are excluded from **both** passes. They are never auto-queued by the ManifestManager, dormant dependents must be explicitly activated at runtime by the parent train via [`IDormantDependentContext`](../dependent-trains.md#dormant-dependents).

Both passes apply the same per-manifest guards before evaluating the schedule:
- Skip if the manifest's ManifestGroup has `IsEnabled = false`
- Skip if the manifest was just dead-lettered this cycle
- Skip if it has an `AwaitingIntervention` dead letter
- Skip if it has a `Queued` work queue entry (already waiting to be dispatched)
- Skip if it has `Pending` or `InProgress` metadata (already running)

`MaxActiveJobs` is deliberately **not** enforced here. The ManifestManager freely identifies all due manifests. The JobDispatcher handles capacity gating at dispatch time. This keeps the two concerns separate, scheduling logic doesn't need to know about system-wide capacity.

### CreateWorkQueueEntriesJunction

For each manifest identified as due, creates a `WorkQueue` entry with:
- `TrainName` from the manifest's `Name` (the canonical interface name, e.g. `MyApp.Trains.IProcessOrderTrain`)
- `Input` / `InputTypeName` from the manifest's `Properties` / `PropertyTypeName`
- `ManifestId` linking back to the source manifest
- `Priority` set from `ManifestGroup.Priority` (the group's priority, not an individual manifest priority)
- `Status = Queued`

For dependent manifests, `DependentPriorityBoost` is still added on top of the group priority at dispatch time.

Each entry is saved individually. If one fails (e.g., a serialization issue for a specific manifest), the others still get queued. Errors are logged per-manifest.

#### Group-Fair Batching

The number of entries created per cycle is limited by `MaxWorkQueueEntriesPerCycle` (default: 200). When more manifests are due than the limit allows, entries are distributed fairly across manifest groups: each group gets a base allocation of `limit / numGroups`, with overflow slots going to higher-priority groups first. This prevents a single large group from monopolizing the batch and starving smaller groups (e.g., 5000 cache manifests crowding out 15 delta manifests). Excess manifests are deferred to the next polling cycle.

Without group-fair batching, if a large group has thousands of simultaneously-eligible manifests (such as after a mass failure with retries), a flat `Take(N)` would consistently select manifests from that group first, leaving smaller groups perpetually deferred.

Set `MaxWorkQueueEntriesPerCycle` to `null` to disable the limit (unlimited, all due manifests are enqueued per cycle, no fairness needed).

## Concurrency Model: Two-Layer Defense

The ManifestManager uses a layered approach to prevent duplicate work queue entries. Each layer addresses a different failure mode.

### Outer Layer: Advisory Lock (Single-Leader Election)

The `ManifestManagerPollingService` acquires a PostgreSQL transaction-scoped advisory lock before invoking the train:

```sql
SELECT pg_try_advisory_xact_lock(hashtext('trax_manifest_manager'))
```

This is a **non-blocking try-lock**: if another server already holds it, the current server skips the cycle entirely and waits for the next polling tick. No server ever blocks waiting for the lock.

The lock is `xact`-scoped (transaction-scoped), meaning it auto-releases when the wrapping transaction commits or rolls back. The entire ManifestManagerTrain runs within this transaction, so all database changes (dead letters, WorkQueue entries) are committed atomically. If the train fails partway through, everything rolls back. No partial state.

This is the primary concurrency control. It guarantees that in a multi-server deployment, only one server evaluates manifests at a time, eliminating the TOCTOU race between `LoadManifestsJunction` (which reads `HasQueuedWork = false`) and `CreateWorkQueueEntriesJunction` (which inserts the entry).

### Inner Layer: Logical State Guards

Even within a single-server cycle, `DetermineJobsToQueueJunction` applies per-manifest guards via `ShouldSkipManifest` before evaluating schedules:

| Guard | Flag | Prevents |
|-------|------|----------|
| Dead-lettered this cycle | `newlyDeadLetteredManifestIds` | Queueing a manifest that was just moved to the dead letter queue |
| Existing dead letter | `HasAwaitingDeadLetter` | Queueing a manifest that requires manual intervention |
| Already queued | `HasQueuedWork` | Duplicate WorkQueue entries for the same manifest |
| Already executing | `HasActiveExecution` | Overlapping runs of the same manifest |

These guards are computed from the database-projected flags in `ManifestDispatchView`. They are **not a replacement** for the advisory lock, without the lock, two servers could simultaneously see `HasQueuedWork = false` for the same manifest and both create entries. The guards protect against logical errors within a single evaluation cycle (e.g., a manifest that appears "due" but is already being handled).

### Backstop: Unique Partial Index

As a final safety net, a unique partial index on the `work_queue` table prevents duplicate `Queued` entries for the same manifest at the database level:

```sql
CREATE UNIQUE INDEX ix_work_queue_unique_queued_manifest
    ON trax.work_queue (manifest_id)
    WHERE status = 'queued' AND manifest_id IS NOT NULL;
```

If the advisory lock is somehow bypassed (e.g., a bug, a code path that doesn't go through the polling service), this index causes a constraint violation on the second insert. The per-entry `try/catch` in `CreateWorkQueueEntriesJunction` catches the error and logs it. No crash, no corruption. Manual WorkQueue entries (`manifest_id IS NULL`) are excluded from this index.

### Non-Postgres Providers

The advisory lock is only acquired when the `IDataContext` is backed by Entity Framework Core (`DbContext`). When using the InMemory provider for tests, the lock is skipped and the train runs directly, safe because InMemory implies a single-process setup.

See [Multi-Server Concurrency](../concurrency.md) for the full cross-service concurrency model.

## What Changed

Previously, this train had an `EnqueueJobsJunction` as its final junction. That junction would directly create Metadata records and enqueue to the job submitter (Hangfire). `MaxActiveJobs` was enforced there, meaning the ManifestManager was both the scheduler and the dispatcher.

Now those responsibilities are split. The ManifestManager writes intent to the work queue. The [JobDispatcher](job-dispatcher.md) reads from it and handles the actual dispatch. This means `TriggerAsync`, dashboard re-runs, and scheduled manifests all converge on the same dispatch path.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
