---
layout: default
title: Administrative Trains
parent: Scheduling
nav_order: 6
has_children: true
---

# Administrative Trains

The scheduler runs four internal trains to manage the job lifecycle. They're registered automatically when you call `AddScheduler`—you never instantiate them yourself. They're excluded from `MaxActiveJobs` counts and filtered out of dashboard statistics by default.

```
AdminTrains.Types:
  - ManifestManagerTrain
  - JobDispatcherTrain
  - JobRunnerTrain
  - MetadataCleanupTrain
```

## The Polling Services

The scheduler registers hosted services based on the configured data provider:

1. **SchedulerStartupService** (`IHostedService`, always registered) — runs once on startup, seeds manifests configured via `.Schedule()`, `.ScheduleMany()`, `.ThenInclude()`, `.ThenIncludeMany()`, `.Include()`, or `.IncludeMany()`. With PostgreSQL, also recovers stuck jobs and cleans up orphaned manifest groups. Seeding failures prevent the host from starting — if your manifest configuration is broken, you want to know immediately.

2. **ManifestManagerPollingService** (`BackgroundService`, always registered) — polls on `ManifestManagerPollingInterval` (default: 5 seconds). With PostgreSQL, runs `ManifestManagerTrain` which evaluates manifests and writes to the work queue. With InMemory, runs `InMemoryManifestManagerTrain` which evaluates manifests and dispatches jobs directly via `InMemoryJobSubmitter` — jobs execute inline during the polling cycle.

3. **JobDispatcherPollingService** (`BackgroundService`, PostgreSQL only) — polls on `JobDispatcherPollingInterval` (default: 5 seconds). Each cycle runs the JobDispatcher train, which reads from the work queue, enforces capacity, and dispatches to the job submitter. Not registered with InMemory — the `InMemoryManifestManagerTrain` dispatches directly.

With PostgreSQL, the ManifestManager and JobDispatcher run independently on their own timers. They communicate through the work queue table — ManifestManager writes entries, JobDispatcher reads them. This means JobDispatcher may not see ManifestManager's freshly-queued entries until its next tick, but no work is lost. Independent intervals allow you to tune each service separately (e.g., fast manifest evaluation with slower dispatch, or vice versa).

In multi-server deployments, each service uses a different concurrency strategy: the ManifestManager uses a PostgreSQL advisory lock for single-leader election, the JobDispatcher uses `FOR UPDATE SKIP LOCKED` for parallel per-entry dispatch, and the cleanup service is naturally idempotent. See [Multi-Server Concurrency](concurrency.md) for details.

> **InMemory note:** The `InMemoryManifestManagerTrain` omits `CancelTimedOutJobsJunction` and `ReapStalePendingMetadataJunction` (which use `ExecuteUpdateAsync`, not supported by InMemory) and replaces `CreateWorkQueueEntriesJunction` with `InMemoryDispatchJobsJunction` (which dispatches jobs inline). This means timeout cancellation and stale-pending recovery are not available with InMemory.

## The Work Queue

All job execution flows through the `work_queue` table. This is the key design decision: nothing goes directly to the job submitter anymore. The ManifestManager doesn't enqueue jobs. `TriggerAsync` doesn't enqueue jobs. Dashboard re-runs don't enqueue jobs. They all write a `WorkQueue` entry with status `Queued`, and the JobDispatcher picks them up.

This gives you a single enforcement point for `MaxActiveJobs`. Before the work queue existed, capacity limits had to be checked in every code path that could trigger a job. Now there's one gateway, and it's the JobDispatcher.

```
┌──────────────────────┐
│  ManifestManager     │──┐
│  (scheduled jobs)    │  │
└──────────────────────┘  │
                          │    ┌─────────────┐    ┌───────────────┐    ┌──────────────┐
┌──────────────────────┐  ├──► │  WORK_QUEUE │──► │ JobDispatcher │──► │ LocalWorkers │
│  TriggerAsync        │──┤    └─────────────┘    └───────────────┘    └──────────────┘
│  (manual trigger)    │  │
└──────────────────────┘  │
                          │
┌──────────────────────┐  │
│  Dashboard           │──┘
│  (re-runs)           │
└──────────────────────┘
```

A `WorkQueue` entry tracks:
- **TrainName**: which train to run (fully qualified type name)
- **Input / InputTypeName**: serialized input and its type, for deserialization at dispatch time
- **Status**: `Queued` → `Dispatched`
- **ManifestId**: optional link back to the originating manifest
- **MetadataId**: set by the JobDispatcher when it creates the execution record

## Excluding Trains from MaxActiveJobs

Administrative trains are excluded from the active job count by default. If you have your own high-frequency internal trains that shouldn't count against the limit, exclude them in the builder:

```csharp
.AddScheduler(scheduler => scheduler
    .MaxActiveJobs(100)
    .ExcludeFromMaxActiveJobs<IMyInternalTrain>()
)
```

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup)
