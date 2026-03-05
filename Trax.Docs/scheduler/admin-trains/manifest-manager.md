---
layout: default
title: ManifestManager
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 1
---

# ManifestManagerTrain

The ManifestManager is the first half of each polling cycle. It figures out which manifests are due for execution and writes them to the work queue. It doesn't dispatch anything—that's the [JobDispatcher's](job-dispatcher.md) job.

## Chain

```
LoadManifests → ReapFailedJobs → DetermineJobsToQueue → CreateWorkQueueEntries
```

## Steps

### LoadManifestsStep

Projects all enabled manifests into lightweight `ManifestDispatchView` records using a single database query with pre-computed aggregate flags (`FailedCount`, `HasAwaitingDeadLetter`, `HasQueuedWork`, `HasActiveExecution`). These flags are computed via COUNT/EXISTS subqueries pushed into the database, keeping query cost O(manifests) regardless of how large the child tables (`Metadatas`, `DeadLetters`, `WorkQueues`) grow.

The projection uses `AsNoTracking()` — the results are read-only snapshots used for scheduling decisions only. No unbounded child collections are loaded into memory.

### ReapFailedJobsStep

Scans loaded manifests for any whose failure count meets or exceeds `MaxRetries`. For each, it creates a `DeadLetter` record with status `AwaitingIntervention` and persists immediately.

A manifest is only reaped if it doesn't already have an unresolved dead letter. This prevents duplicate dead letters from accumulating when the same manifest fails across multiple polling cycles.

The step returns the list of newly created dead letters so `DetermineJobsToQueueStep` can skip those manifests without re-querying the database.

### DetermineJobsToQueueStep

The decision step. It runs two passes over the loaded manifests:

**Pass 1: Time-based manifests** (Cron and Interval). For each, it checks whether the manifest is due using `SchedulingHelpers.ShouldRunNow()`, which dispatches to either cron parsing or interval arithmetic based on the schedule type.

**Pass 2: Dependent manifests**. For each manifest with `ScheduleType.Dependent`, it finds the parent in the loaded set and checks whether `parent.LastSuccessfulRun > dependent.LastSuccessfulRun`. See [Dependent Trains](../dependent-trains.md).

Manifests with `ScheduleType.DormantDependent` are excluded from **both** passes. They are never auto-queued by the ManifestManager—dormant dependents must be explicitly activated at runtime by the parent train via [`IDormantDependentContext`](../dependent-trains.md#dormant-dependents).

Both passes apply the same per-manifest guards before evaluating the schedule:
- Skip if the manifest's ManifestGroup has `IsEnabled = false`
- Skip if the manifest was just dead-lettered this cycle
- Skip if it has an `AwaitingIntervention` dead letter
- Skip if it has a `Queued` work queue entry (already waiting to be dispatched)
- Skip if it has `Pending` or `InProgress` metadata (already running)

`MaxActiveJobs` is deliberately **not** enforced here. The ManifestManager freely identifies all due manifests. The JobDispatcher handles capacity gating at dispatch time. This keeps the two concerns separate—scheduling logic doesn't need to know about system-wide capacity.

### CreateWorkQueueEntriesStep

For each manifest identified as due, creates a `WorkQueue` entry with:
- `TrainName` from the manifest's `Name`
- `Input` / `InputTypeName` from the manifest's `Properties` / `PropertyTypeName`
- `ManifestId` linking back to the source manifest
- `Priority` set from `ManifestGroup.Priority` (the group's priority, not an individual manifest priority)
- `Status = Queued`

For dependent manifests, `DependentPriorityBoost` is still added on top of the group priority at dispatch time.

Each entry is saved individually. If one fails (e.g., a serialization issue for a specific manifest), the others still get queued. Errors are logged per-manifest.

## Concurrency Model: Two-Layer Defense

The ManifestManager uses a layered approach to prevent duplicate work queue entries. Each layer addresses a different failure mode.

### Outer Layer: Advisory Lock (Single-Leader Election)

The `ManifestManagerPollingService` acquires a PostgreSQL transaction-scoped advisory lock before invoking the train:

```sql
SELECT pg_try_advisory_xact_lock(hashtext('trax_manifest_manager'))
```

This is a **non-blocking try-lock** — if another server already holds it, the current server skips the cycle entirely and waits for the next polling tick. No server ever blocks waiting for the lock.

The lock is `xact`-scoped (transaction-scoped), meaning it auto-releases when the wrapping transaction commits or rolls back. The entire ManifestManagerTrain runs within this transaction, so all database changes (dead letters, WorkQueue entries) are committed atomically. If the train fails partway through, everything rolls back — no partial state.

This is the primary concurrency control. It ensures that in a multi-server deployment, only one server evaluates manifests at a time, eliminating the TOCTOU race between `LoadManifestsStep` (which reads `HasQueuedWork = false`) and `CreateWorkQueueEntriesStep` (which inserts the entry).

### Inner Layer: Logical State Guards

Even within a single-server cycle, `DetermineJobsToQueueStep` applies per-manifest guards via `ShouldSkipManifest` before evaluating schedules:

| Guard | Flag | Prevents |
|-------|------|----------|
| Dead-lettered this cycle | `newlyDeadLetteredManifestIds` | Queueing a manifest that was just moved to the dead letter queue |
| Existing dead letter | `HasAwaitingDeadLetter` | Queueing a manifest that requires manual intervention |
| Already queued | `HasQueuedWork` | Duplicate WorkQueue entries for the same manifest |
| Already executing | `HasActiveExecution` | Overlapping runs of the same manifest |

These guards are computed from the database-projected flags in `ManifestDispatchView`. They are **not a replacement** for the advisory lock — without the lock, two servers could simultaneously see `HasQueuedWork = false` for the same manifest and both create entries. The guards protect against logical errors within a single evaluation cycle (e.g., a manifest that appears "due" but is already being handled).

### Backstop: Unique Partial Index

As a final safety net, a unique partial index on the `work_queue` table prevents duplicate `Queued` entries for the same manifest at the database level:

```sql
CREATE UNIQUE INDEX ix_work_queue_unique_queued_manifest
    ON trax.work_queue (manifest_id)
    WHERE status = 'queued' AND manifest_id IS NOT NULL;
```

If the advisory lock is somehow bypassed (e.g., a bug, a code path that doesn't go through the polling service), this index causes a constraint violation on the second insert. The per-entry `try/catch` in `CreateWorkQueueEntriesStep` catches the error and logs it — no crash, no corruption. Manual WorkQueue entries (`manifest_id IS NULL`) are excluded from this index.

### Non-Postgres Providers

The advisory lock is only acquired when the `IDataContext` is backed by Entity Framework Core (`DbContext`). When using the InMemory provider for tests, the lock is skipped and the train runs directly — safe because InMemory implies a single-process setup.

See [Multi-Server Concurrency](../concurrency.md) for the full cross-service concurrency model.

## What Changed

Previously, this train had an `EnqueueJobsStep` as its final step. That step would directly create Metadata records and enqueue to the job submitter (Hangfire). `MaxActiveJobs` was enforced there, meaning the ManifestManager was both the scheduler and the dispatcher.

Now those responsibilities are split. The ManifestManager writes intent to the work queue. The [JobDispatcher](job-dispatcher.md) reads from it and handles the actual dispatch. This means `TriggerAsync`, dashboard re-runs, and scheduled manifests all converge on the same dispatch path.
