---
layout: default
title: Administrative Workflows
parent: Scheduling
nav_order: 6
has_children: true
---

# Administrative Workflows

The scheduler runs four internal workflows to manage the job lifecycle. They're registered automatically when you call `AddScheduler`—you never instantiate them yourself. They're excluded from `MaxActiveJobs` counts and filtered out of dashboard statistics by default.

```
AdminWorkflows.Types:
  - ManifestManagerWorkflow
  - JobDispatcherWorkflow
  - TaskServerExecutorWorkflow
  - MetadataCleanupWorkflow
```

## The Polling Services

The scheduler runs three hosted services:

1. **SchedulerStartupService** (`IHostedService`) — runs once on startup, seeds manifests configured via `.Schedule()`, `.ScheduleMany()`, `.ThenInclude()`, `.ThenIncludeMany()`, `.Include()`, or `.IncludeMany()`, recovers stuck jobs, and cleans up orphaned manifest groups. Seeding failures prevent the host from starting — if your manifest configuration is broken, you want to know immediately.

2. **ManifestManagerPollingService** (`BackgroundService`) — polls on `ManifestManagerPollingInterval` (default: 5 seconds). Each cycle runs the ManifestManager workflow, which evaluates which manifests are due and writes to the work queue.

3. **JobDispatcherPollingService** (`BackgroundService`) — polls on `JobDispatcherPollingInterval` (default: 5 seconds). Each cycle runs the JobDispatcher workflow, which reads from the work queue, enforces capacity, and dispatches to the background task server.

The ManifestManager and JobDispatcher run independently on their own timers. They communicate through the work queue table — ManifestManager writes entries, JobDispatcher reads them. This means JobDispatcher may not see ManifestManager's freshly-queued entries until its next tick, but no work is lost. Independent intervals allow you to tune each service separately (e.g., fast manifest evaluation with slower dispatch, or vice versa).

In multi-server deployments, each service uses a different concurrency strategy: the ManifestManager uses a PostgreSQL advisory lock for single-leader election, the JobDispatcher uses `FOR UPDATE SKIP LOCKED` for parallel per-entry dispatch, and the cleanup service is naturally idempotent. See [Multi-Server Concurrency](concurrency.md) for details.

## The Work Queue

All job execution flows through the `work_queue` table. This is the key design decision: nothing goes directly to the background task server anymore. The ManifestManager doesn't enqueue jobs. `TriggerAsync` doesn't enqueue jobs. Dashboard re-runs don't enqueue jobs. They all write a `WorkQueue` entry with status `Queued`, and the JobDispatcher picks them up.

This gives you a single enforcement point for `MaxActiveJobs`. Before the work queue existed, capacity limits had to be checked in every code path that could trigger a job. Now there's one gateway, and it's the JobDispatcher.

```
┌──────────────────────┐
│  ManifestManager     │──┐
│  (scheduled jobs)    │  │
└──────────────────────┘  │
                          │    ┌─────────────┐    ┌───────────────┐    ┌──────────┐
┌──────────────────────┐  ├──► │  WORK_QUEUE │──► │ JobDispatcher │──► │ Hangfire │
│  TriggerAsync        │──┤    └─────────────┘    └───────────────┘    └──────────┘
│  (manual trigger)    │  │
└──────────────────────┘  │
                          │
┌──────────────────────┐  │
│  Dashboard           │──┘
│  (re-runs)           │
└──────────────────────┘
```

A `WorkQueue` entry tracks:
- **WorkflowName**: which workflow to run (fully qualified type name)
- **Input / InputTypeName**: serialized input and its type, for deserialization at dispatch time
- **Status**: `Queued` → `Dispatched`
- **ManifestId**: optional link back to the originating manifest
- **MetadataId**: set by the JobDispatcher when it creates the execution record

## Excluding Workflows from MaxActiveJobs

Administrative workflows are excluded from the active job count by default. If you have your own high-frequency internal workflows that shouldn't count against the limit, exclude them in the builder:

```csharp
.AddScheduler(scheduler => scheduler
    .MaxActiveJobs(100)
    .ExcludeFromMaxActiveJobs<IMyInternalWorkflow>()
)
```

*API Reference: [AddScheduler]({{ site.baseurl }}{% link api-reference/scheduler-api/add-scheduler.md %}), [AddMetadataCleanup]({{ site.baseurl }}{% link api-reference/scheduler-api/add-metadata-cleanup.md %})*
