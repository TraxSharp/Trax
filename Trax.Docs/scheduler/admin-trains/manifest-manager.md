---
layout: default
title: ManifestManager
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 1
---

# ManifestManagerTrain

The ManifestManager is the first half of each polling cycle. It figures out which manifests are due for execution and writes them to the work queue. It doesn't dispatch anything; that's the [JobDispatcher's](/docs/scheduler/admin-trains/job-dispatcher) job.

## Chain

```
CancelTimedOutJobsJunction → ReapStalePendingMetadataJunction → ReapStaleInProgressMetadataJunction → LoadManifestsJunction → ResolveStaleStagedEntriesJunction → ReapFailedJobsJunction → DetermineJobsToQueueJunction → CreateWorkQueueEntriesJunction
```

## Junctions

The timeout and stale-run junctions come first and work on runs directly, not on the loaded manifests, so the manifests are loaded after them and a run they fail is already counted in the same cycle.

### CancelTimedOutJobsJunction

Finds every InProgress run past its timeout and requests cooperative cancellation. A run's timeout is its manifest's `TimeoutSeconds`, or `DefaultJobTimeout` when the manifest sets none or the run has no manifest. Runs of disabled manifests are included, and the scheduler's own trains are not. Sets `CancellationRequested = true` in the database (picked up by `CancellationCheckProvider` at the next junction boundary) and attempts same-server instant cancellation via the `CancellationRegistry`.

### ReapStalePendingMetadataJunction

Fails Pending metadata that has not been picked up within `StalePendingTimeout` (default: 20 minutes). Acts as a safety net for dispatch failures where the worker never started executing the job (e.g., remote worker unreachable, Lambda crashed after receiving the request).

### ReapStaleInProgressMetadataJunction

Fails InProgress metadata that has not completed within `StaleInProgressTimeout` (default: 60 minutes). Acts as a safety net for hard crashes. Lambda hard-kills, OOM events, or process crashes where the worker dies without reaching `FinishServiceTrain`. This timeout should be longer than `DefaultJobTimeout` to allow cooperative cancellation (via `CancelTimedOutJobsJunction`) to propagate before force-failing.

A run whose manifest sets a `Timeout` longer than `DefaultJobTimeout` gets as much longer: it is failed at the later of `StaleInProgressTimeout` and its manifest's timeout plus the same grace (`StaleInProgressTimeout - DefaultJobTimeout`, 40 minutes with the defaults). A manifest with a three hour timeout therefore has its run failed as stale at 3 h 40 min, not at 60 minutes while it is still working.

Newly-failed metadata from both stale reapers is counted by `LoadManifestsJunction` in the same ManifestManager cycle, enabling dead-lettering if retries are exhausted.

Failing a run, from either reaper, also releases its [subject key](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing), so a run still pending past `StalePendingTimeout` or still working past its stale threshold stops holding its subject.

### LoadManifestsJunction

Projects all enabled manifests into lightweight `ManifestDispatchView` records using a single database query with pre-computed aggregate flags (`FailedCount`, `HasAwaitingDeadLetter`, `HasQueuedWork`, `HasActiveExecution`, `HasSuccessfulMetadata`) and the end time of the manifest's latest cancelled run. These flags are computed via COUNT/EXISTS subqueries pushed into the database, keeping query cost O(manifests) regardless of how large the child tables (`Metadatas`, `DeadLetters`, `WorkQueues`) grow.

The projection uses `AsNoTracking()`, the results are read-only snapshots used for scheduling decisions only. No unbounded child collections are loaded into memory.

> **Scaling note:** LoadManifestsJunction loads all enabled manifests in a single query. For typical production deployments (up to 10K manifests), this is efficient with proper indexes.

### ResolveStaleStagedEntriesJunction

Resolves work queue entries that a crash left unconfirmed. A train with [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable) commits its entry unconfirmed, runs `OnQueue`, then confirms it; a process that stopped in between leaves an entry the dispatcher will never claim. Any entry still `Queued` and unconfirmed after `StaleStagedEntryTimeout` (default: 10 minutes) is:

| Setting | Outcome |
|---|---|
| Default | **Cancelled**, via `IWorkQueuePromotion.CancelStaleAsync`. Nothing recorded tells a hook that succeeded from one that never ran or one that rejected the mutation, so the entry is kept visible rather than run. The same call deletes the entries it cancelled on an earlier pass once they are 30 days old ([retention](/docs/sdk-reference/scheduler-api/i-work-queue-promotion#retention-of-cancelled-staged-entries)) |
| `PromoteStaleStagedEntries()` | **Promoted**, via `IWorkQueuePromotion.PromoteStaleAsync`, and dispatched like any other entry. The run re-executes the whole chain, so this assumes idempotent hooks and a chain that re-checks what the hook checked |

`IWorkQueuePromotion.PromoteAsync`, which an enqueue calls once its hook returns, only confirms an entry that is still `Queued`, so an entry this junction has already cancelled stays cancelled. The promotion methods work on every data provider, the InMemory provider included. Like the rest of the train, this runs only on the server holding the leader lock, and not at all while the ManifestManager is disabled (`ManifestManagerEnabled = false`). A deployment that turns the ManifestManager off everywhere leaves stranded staged entries unresolved.

### ReapFailedJobsJunction

Scans loaded manifests for any whose failure count exceeds `MaxRetries`. `MaxRetries` is the number of retries after the first run, so `MaxRetries(0)` dead-letters on the first failure and the default of 3 on the fourth; a manifest with no counted failure is never dead-lettered. For each, it creates a `DeadLetter` record with status `AwaitingIntervention` and persists immediately.

A manifest is only reaped if it doesn't already have an unresolved dead letter. This prevents duplicate dead letters from accumulating when the same manifest fails across multiple polling cycles.

`FailedCount` only counts failures that started within [`FailureCountWindow`](/docs/scheduler/scheduling-options#configuration-options) (default: 24 hours) **and after** the most recent dead letter resolution. When a dead letter is resolved (retried or acknowledged), the failure counter effectively resets, only new failures contribute toward the next `MaxRetries` threshold. This prevents retried manifests from being immediately re-dead-lettered due to historical failures that were already addressed. The window does the same for failures that were never dead-lettered: a failure a month ago neither delays the next run nor counts toward a dead letter, even though a success does not reset the count inside the window.

The junction returns the list of newly created dead letters so `DetermineJobsToQueueJunction` can skip those manifests without re-querying the database.

### DetermineJobsToQueueJunction

The decision junction. It runs two passes over the loaded manifests:

**Pass 1: Time-based manifests** (Cron and Interval). For each, it checks whether the manifest is due using `SchedulingHelpers.ShouldRunNow()`, which dispatches to either cron parsing or interval arithmetic based on the schedule type. Cron is evaluated in UTC. A cron that has never succeeded is due at its first occurrence after it was scheduled, which scheduling records on the manifest's `NextScheduledRun`. The schedule is evaluated from the later of the manifest's last successful run and its last cancelled run, because a cancelled run consumes the occurrence it ran for: after a cancelled run the next occurrence after it is due, whatever `NextScheduledRun` held.

**Pass 2: Dependent manifests**. For each manifest with `ScheduleType.Dependent`, it finds the parent in the loaded set and checks whether the parent's `LastSuccessfulRun` is later than the dependent's last run. That is the start of the dependent's latest successful run (`LoadManifestsJunction` loads that start time for dependents only; with none on record, the dependent's own `LastSuccessfulRun` stands in), so a parent success that landed while the dependent was running queues it again; or the end of its latest cancelled run when that is later, because a cancelled run consumed the parent success it was started for. Before comparing timestamps, the junction verifies that the parent has at least one `Completed` metadata record (`HasSuccessfulMetadata`). If the parent has a `LastSuccessfulRun` timestamp but no successful metadata to back it up (e.g., metadata was truncated or pruned), the timestamp is considered stale and the dependent is not queued. See [Dependent Trains](/docs/scheduler/dependent-trains).

Manifests with `ScheduleType.DormantDependent` are excluded from **both** passes. They are never auto-queued by the ManifestManager, dormant dependents must be explicitly activated at runtime by the parent train via [`IDormantDependentContext`](/docs/scheduler/dependent-trains#dormant-dependents).

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
- `Priority` set from the manifest's own `Priority`, as a manual trigger or a dead-letter requeue is (the dispatcher orders by the group's priority first)
- `Status = Queued`

For dependent manifests, `DependentPriorityBoost` is added on top of the manifest priority.

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

See [Multi-Server Concurrency](/docs/scheduler/concurrency) for the full cross-service concurrency model.

## What Changed

Previously, this train had an `EnqueueJobsJunction` as its final junction. That junction would directly create Metadata records and enqueue to the job submitter (Hangfire). `MaxActiveJobs` was enforced there, meaning the ManifestManager was both the scheduler and the dispatcher.

Now those responsibilities are split. The ManifestManager writes intent to the work queue. The [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher) reads from it and handles the actual dispatch. This means `TriggerAsync`, dashboard re-runs, and scheduled manifests all converge on the same dispatch path.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
