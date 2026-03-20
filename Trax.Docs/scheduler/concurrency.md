---
layout: default
title: Multi-Server Concurrency
parent: Scheduling
nav_order: 7
---

# Multi-Server Concurrency

Trax.Core's scheduler is safe to run across multiple server instances sharing the same PostgreSQL database. Each polling service uses a different concurrency strategy matched to its semantics — advisory locks for leader election, row-level locking for parallel dispatch, and idempotent operations where neither is needed.

This page documents the concurrency model, the guarantees it provides, and the implications for multi-server deployments.

{: .sdk-references }
> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers)

## Overview

| Service | Strategy | Parallelism | Guarantee |
|---------|----------|-------------|-----------|
| **ManifestManagerPollingService** | Advisory lock (single-leader) | One server per cycle | No duplicate WorkQueue entries |
| **JobDispatcherPollingService** | `FOR UPDATE SKIP LOCKED` (per-entry) | All servers dispatch concurrently | No duplicate Metadata or double-dispatch |
| **LocalWorkerService** | `FOR UPDATE SKIP LOCKED` (per-job) | All servers execute concurrently | No duplicate job execution |
| **MetadataCleanupPollingService** | None (idempotent) | All servers run concurrently | Deleting already-deleted rows is a no-op |

## ManifestManager: Advisory Lock

### The Problem

The ManifestManager evaluates which manifests are "due" for execution and writes WorkQueue entries. If two servers run the ManifestManager simultaneously, they both load the same manifests, both evaluate them as due, and both insert duplicate WorkQueue entries — causing the same train to be dispatched twice.

The race window exists between `LoadManifestsJunction` (which reads `HasQueuedWork = false`) and `CreateWorkQueueEntriesJunction` (which inserts the entry). This is a classic time-of-check-time-of-use (TOCTOU) bug.

### The Solution

The ManifestManagerPollingService acquires a PostgreSQL transaction-scoped advisory lock before running the train:

```sql
SELECT pg_try_advisory_xact_lock(hashtext('trax_manifest_manager'))
```

This is a **non-blocking try-lock**: if another server already holds the lock, the current server skips the cycle and waits for the next polling tick. No server ever blocks waiting for the lock.

```
Server A                              Server B
────────                              ────────
BEGIN TRANSACTION
pg_try_advisory_xact_lock → true ✓
  LoadManifests                       BEGIN TRANSACTION
  ReapFailedJobs                      pg_try_advisory_xact_lock → false ✗
  DetermineJobsToQueue                "Another server is running ManifestManager,
  CreateWorkQueueEntries               skipping cycle"
COMMIT (releases lock)                ROLLBACK
                                      (waits for next polling tick)
```

### How Advisory Locks Work

PostgreSQL advisory locks are application-level locks managed by the database but not tied to any table or row. They come in two flavors:

- **Session-level** (`pg_advisory_lock`): held until explicitly released or the connection closes. Risky with connection pooling — if the connection returns to the pool with the lock held, it stays held until the connection is eventually closed.
- **Transaction-scoped** (`pg_try_advisory_xact_lock`): automatically released when the transaction commits or rolls back. This is what Trax.Core uses — no risk of leaked locks.

The lock key is `hashtext('trax_manifest_manager')`, which produces a stable 32-bit integer from the string. This key is unique to Trax.Core's ManifestManager — other applications using advisory locks on the same database would need to use the same key to conflict (which is astronomically unlikely with a descriptive string).

### Transaction Scope

The advisory lock wraps the entire ManifestManager train in a single transaction. This has two implications:

1. **Atomicity**: All `SaveChanges()` calls within the train junctions (ReapFailedJobsJunction, CreateWorkQueueEntriesJunction) are buffered within the transaction. If the train fails partway through, everything rolls back — no partial state (e.g., dead letters created but WorkQueue entries missing).

2. **Visibility delay**: WorkQueue entries created by CreateWorkQueueEntriesJunction are not visible to the JobDispatcher until the ManifestManager transaction commits. This is typically a few milliseconds of additional latency. The JobDispatcher picks them up on its next polling tick — no work is lost.

### Non-Postgres Providers

The advisory lock is only acquired when the `IDataContext` is backed by Entity Framework Core (i.e., it inherits from `DbContext`). When using the InMemory provider for tests, the lock is skipped entirely and the train runs directly. This is safe because InMemory implies a single-server, single-process setup.

### Defense-in-Depth: Unique Partial Index

As an additional safety net, a unique partial index on the `work_queue` table prevents duplicate `Queued` entries for the same manifest at the database level:

```sql
CREATE UNIQUE INDEX ix_work_queue_unique_queued_manifest
    ON trax.work_queue (manifest_id)
    WHERE status = 'queued' AND manifest_id IS NOT NULL;
```

If the advisory lock is somehow bypassed (e.g., a bug, a code path that doesn't go through the polling service), this index causes a constraint violation on the second insert. The existing per-entry `try/catch` in `CreateWorkQueueEntriesJunction` catches the error and logs it — no crash, no corruption.

Manual WorkQueue entries (from the dashboard or `TriggerAsync`) have `manifest_id IS NULL` and are excluded from this index. Multiple manual triggers for different purposes are always allowed.

## JobDispatcher: Row-Level Locking

### The Problem

The JobDispatcher loads `Queued` WorkQueue entries and dispatches them — creating Metadata records, updating entry status to `Dispatched`, and enqueuing to the job submitter. If two servers load the same entries simultaneously, both would create Metadata records for the same entry and dispatch the train twice.

### The Solution

The DispatchJobsJunction uses PostgreSQL's `FOR UPDATE SKIP LOCKED` to atomically claim each WorkQueue entry before dispatching it. Each entry is processed within its own DI scope and database transaction:

```sql
SELECT * FROM trax.work_queue
WHERE id = :entry_id AND status = 'queued'
FOR UPDATE SKIP LOCKED
```

If the entry has already been claimed by another server (either locked in another transaction or already updated to `Dispatched`), the query returns no rows and the dispatcher skips it.

```
Server A                              Server B
────────                              ────────
Load queued entries [1, 2, 3]         Load queued entries [1, 2, 3]

BEGIN TRANSACTION                     BEGIN TRANSACTION
SELECT ... WHERE id=1 FOR UPDATE      SELECT ... WHERE id=1 FOR UPDATE
  SKIP LOCKED → row returned ✓          SKIP LOCKED → skipped (locked) ✗
  Create Metadata                     SELECT ... WHERE id=2 FOR UPDATE
  Update status → Dispatched            SKIP LOCKED → row returned ✓
COMMIT                                  Create Metadata
Enqueue to job submitter                  Update status → Dispatched
                                      COMMIT
BEGIN TRANSACTION                     Enqueue to job submitter
SELECT ... WHERE id=2 FOR UPDATE
  SKIP LOCKED → skipped (already      BEGIN TRANSACTION
  dispatched, status ≠ 'queued') ✗    SELECT ... WHERE id=3 FOR UPDATE
SELECT ... WHERE id=3 FOR UPDATE        SKIP LOCKED → row returned ✓
  SKIP LOCKED → skipped (locked) ✗      ...
                                      COMMIT
                                      Enqueue to job submitter
```

### Why Not an Advisory Lock?

Unlike the ManifestManager, the JobDispatcher benefits from **parallel dispatch** across servers. Each server can claim and dispatch different entries simultaneously, increasing throughput. An advisory lock would serialize all dispatch activity to a single server — wasteful when the work queue has many entries.

The `FOR UPDATE SKIP LOCKED` pattern allows fine-grained, per-entry parallelism: multiple servers work through the queue concurrently, each atomically claiming the next available entry. This is the same pattern used by the [LocalWorkerService](job-submission.md#worker-lifecycle) for job execution.

### Intra-Cycle Parallelism

In addition to multi-server parallelism, a single server can dispatch multiple entries concurrently within a single polling cycle via `MaxConcurrentDispatch`. This is particularly important when using `UseRemoteWorkers()`, where each dispatch blocks on an HTTP POST. Without intra-cycle parallelism, dispatching 50 entries at 2 seconds each takes ~100 seconds — blocking the polling service from starting the next cycle.

With `MaxConcurrentDispatch(10)`, the same 50 entries are dispatched in ~10 seconds (5 batches of 10). The `FOR UPDATE SKIP LOCKED` pattern ensures safety — concurrent dispatches within the same cycle cannot claim the same entry, just as concurrent dispatches across servers cannot.

See [JobDispatcher — Parallel Dispatch](admin-trains/job-dispatcher.md#parallel-dispatch) for configuration details.

### Per-Entry DI Scope

Each entry is dispatched within its own DI scope, following the same pattern as the LocalWorkerService. This provides:

1. **Clean change tracker**: each entry gets a fresh `IDataContext` with no stale tracked entities from previous iterations.
2. **Transaction isolation**: if one entry fails, its transaction is rolled back without affecting others.
3. **Commit-then-enqueue**: the claim transaction (Metadata creation + WorkQueue status update) is committed before calling `EnqueueAsync` on the job submitter. This ensures the Metadata record is visible to the job submitter when it begins execution — necessary because the `InMemoryJobSubmitter` executes trains synchronously within `EnqueueAsync`. If the enqueue fails after commit, the WorkQueue entry is already `Dispatched` with a valid Metadata record; the next dispatch cycle won't re-process it, but the Metadata's `Pending` state can be detected for recovery.

### Capacity Limit Approximation

With multiple servers, `MaxActiveJobs` enforcement is approximate. Each server independently counts active Metadata records in `LoadDispatchCapacityJunction`. Between the count and the actual dispatch, another server may have dispatched entries, causing the total to slightly exceed the configured limit.

This is a deliberate tradeoff. `MaxActiveJobs` is a soft limit to prevent overwhelming the system — not a strict concurrency semaphore. The alternative (a global advisory lock for the entire dispatch cycle) would serialize all dispatch activity, defeating the purpose of multi-server deployment.

In practice, the overshoot is bounded by the number of servers multiplied by the number of entries dispatched per cycle. For most deployments, this is negligible.

## LocalWorkerService: Already Safe

The LocalWorkerService has used `FOR UPDATE SKIP LOCKED` since its introduction. Multiple worker threads (across one or many servers) atomically claim jobs from the `background_job` table. Each claim is a separate transaction: lock the row, set `fetched_at`, commit. Other workers skip locked rows and move to the next available job.

See [Job Submission — Worker Lifecycle](job-submission.md#worker-lifecycle) for the full dequeue SQL and crash recovery details.

## MetadataCleanupPollingService: Idempotent

The cleanup service deletes old Metadata records based on a retention period. Multiple servers can run cleanup concurrently without conflict — deleting an already-deleted row is a no-op. No locking is needed.

## Deployment Considerations

### Minimum Configuration

No configuration changes are needed for multi-server deployments. The concurrency controls are always active — advisory locks and row-level locking work correctly even with a single server (the lock is always acquired, the `FOR UPDATE` always succeeds).

### Polling Interval Tuning

With multiple servers, consider the polling interval for the ManifestManager. Since only one server runs the ManifestManager per cycle (advisory lock), having many servers poll frequently means many lock acquisition attempts that return `false`. This is cheap (a single SQL call that returns immediately), but if you want to reduce noise in logs, you can increase the interval:

```csharp
.AddScheduler(scheduler => scheduler
    .ManifestManagerPollingInterval(TimeSpan.FromSeconds(10))
)
```

The JobDispatcher polling interval doesn't need adjustment — multiple servers processing the work queue concurrently is the desired behavior.

### Database Connection Pooling

Advisory locks are transaction-scoped, so they don't interact with connection pooling. When a transaction commits or rolls back, the lock is released regardless of what happens to the underlying connection. No special pooling configuration is needed.

### Monitoring

In a multi-server deployment, you'll see these log messages:

```
# Server that acquires the lock:
ManifestManager polling cycle starting
ManifestManager polling cycle completed

# Servers that skip:
Another server is running ManifestManager, skipping cycle

# JobDispatcher (on any server):
Work queue entry {id} already claimed by another server, skipping
```

These are `Debug`-level messages. In production, set the log level to `Information` or higher to suppress them.

## Summary of Guarantees

| Scenario | Guarantee | Mechanism |
|----------|-----------|-----------|
| Two servers evaluate the same manifest as "due" | Only one creates a WorkQueue entry | Advisory lock + unique partial index |
| Two servers try to dispatch the same WorkQueue entry | Only one creates the Metadata and enqueues | `FOR UPDATE SKIP LOCKED` |
| Two workers try to execute the same BackgroundJob | Only one claims and runs it | `FOR UPDATE SKIP LOCKED` |
| Two servers run metadata cleanup concurrently | Both succeed, no side effects | Idempotent deletes |
| A server crashes mid-ManifestManager cycle | Transaction rolls back, lock released, no partial state | Transaction-scoped advisory lock |
| A server crashes mid-dispatch of a WorkQueue entry | Transaction rolls back, entry remains `Queued` for next cycle | Per-entry transaction |
| A worker crashes mid-execution of a BackgroundJob | Visibility timeout expires, job reclaimed by another worker | `fetched_at` timestamp |
