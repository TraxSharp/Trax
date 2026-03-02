---
layout: default
title: Scheduling
nav_order: 6
has_children: true
---

# Scheduling

Trax.Scheduler adds background job orchestration to trains. Define a manifest (what to run, when, and how many retries), and the scheduler handles execution, retries, and dead-lettering.

This isn't a traditional cron scheduler. It supports cron expressions, but its design goal is controlled bulk job orchestration—database replication with thousands of table slices, for example—where you need visibility into every execution attempt.

## When to Use the Scheduler

A hosted service with a timer works fine for simple recurring tasks. The Scheduler is for when you need the audit trail: every execution recorded with inputs, outputs, timing, and failure details. Failed jobs retry automatically. Jobs that fail too many times go to a dead letter queue for manual review.

## Core Concepts

### Manifest = Job Definition

A `Manifest` describes a type of job: which train it triggers, scheduling rules, retry policies, and default configuration. The `IManifestScheduler` handles the boilerplate—no need to worry about assembly-qualified names or JSON serialization:

```csharp
await scheduler.ScheduleAsync<ISyncCustomersTrain, SyncCustomersInput>(
    "sync-customers-us-east",
    new SyncCustomersInput { Region = "us-east", BatchSize = 500 },
    Every.Hours(6),
    opts => opts.MaxRetries = 3);

// For bulk scheduling from a collection, use ScheduleMany:
scheduler.ScheduleMany<ISyncTableTrain, SyncTableInput, string>(
    "sync",
    tables,
    table => (table, new SyncTableInput { TableName = table }),
    Every.Minutes(5));
```

*API Reference: [ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}), [ScheduleMany]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %})*

The scheduler creates the manifest, resolves the correct type names, and serializes the input automatically. Every call is an upsert—safe to run on every startup without duplicating jobs.

### Metadata = Execution Record

Each time a manifest runs, it creates a new `Metadata` record. These are **immutable**—retries create new rows, never mutate existing ones. This gives you a complete audit trail:

```
Manifest: "sync-customers-us-east"
├── Metadata #1: Completed at 10:00:00
├── Metadata #2: Failed at 10:05:00 (timeout)
├── Metadata #3: Failed at 10:10:00 (timeout)
├── Metadata #4: Failed at 10:15:00 (timeout) → Dead-lettered
```

### Dead Letter = Failed Beyond Retry

When a job fails more times than `MaxRetries`, it moves to the dead letter queue. Dead letters require manual intervention—the scheduler won't automatically retry them.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Dead Letter                               │
├─────────────────────────────────────────────────────────────────┤
│ Status: AwaitingIntervention                                    │
│ Reason: Max retries exceeded (3 failures >= 3 max retries)      │
│ DeadLetteredAt: 2026-02-10 10:15:00                            │
└─────────────────────────────────────────────────────────────────┘
```

Operators can retry (which creates a new execution) or acknowledge (mark as handled without retry).

### Dependent Manifests

A manifest can depend on another manifest. Instead of running on a timer, it fires when its parent's `LastSuccessfulRun` advances past the dependent's own. This is how you build ETL chains, post-processing steps, or any train that should only run after another succeeds. See [Dependent Trains](scheduler/dependent-trains.md).

```csharp
scheduler
    .Schedule<IExtractTrain, ExtractInput>(
        "extract", new ExtractInput(), Every.Hours(1))
    .Include<ILoadTrain, LoadInput>(
        "load", new LoadInput());
```

*API Reference: [Schedule]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}), [Include]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %})*

### Manifest Groups

Every manifest belongs to a `ManifestGroup`. Groups provide per-group dispatch controls — `MaxActiveJobs`, `Priority`, and `IsEnabled` — configurable from the dashboard. When a group hits its concurrency cap, the dispatcher skips it and continues processing other groups, preventing starvation.

```csharp
scheduler
    .Schedule<ISyncTrain, SyncInput>(
        "sync-data", new SyncInput(), Every.Hours(1),
        groupId: "data-sync")
    .Include<ILoadTrain, LoadInput>(
        "load-data", new LoadInput(),
        groupId: "data-sync");
```

When `groupId` is not specified, it defaults to the manifest's `externalId`. See [Scheduling Options](scheduler/scheduling-options.md#per-group-dispatch-controls).

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│            SchedulerStartupService (IHostedService)              │
│     RecoverStuckJobs + SeedPendingManifests (runs once)          │
└─────────────────────────────────────────────────────────────────┘

┌──────────────────────────────┐  ┌──────────────────────────────┐
│ ManifestManagerPollingService│  │ JobDispatcherPollingService   │
│ (BackgroundService)          │  │ (BackgroundService)           │
│ Polls on its own interval    │  │ Polls on its own interval     │
└──────────────┬───────────────┘  └──────────────┬────────────────┘
               ▼                                  ▼
┌──────────────────────────────┐  ┌──────────────────────────────┐
│   ManifestManagerTrain    │  │    JobDispatcherTrain      │
│                              │  │                               │
│  LoadManifests               │  │  LoadQueuedJobs               │
│  → ReapFailedJobs            │  │  → DispatchJobs               │
│  → DetermineJobsToQueue      │  │    (enforces global and       │
│  → CreateWorkQueueEntries    │  │     per-group MaxActiveJobs)  │
└──────────────┬───────────────┘  └──────────────┬────────────────┘
               │                                  │
               │ writes to                        │ reads from
               ▼                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                        WORK_QUEUE                                │
│            Decouples scheduling from dispatch                    │
└─────────────────────────────────┬───────────────────────────────┘
                                  │ dispatched to
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                  TaskServerExecutorTrain                       │
│            (runs on PostgresWorkerService workers)                │
│                                                                  │
│  LoadMetadata → ValidateState → ExecuteTrain →                │
│                                      UpdateManifest              │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Your Train                                 │
│              (Resolved via TrainBus)                           │
└─────────────────────────────────────────────────────────────────┘
```

The **SchedulerStartupService** is an `IHostedService` that runs once on startup. It seeds any manifests configured via `.Schedule()`, `.ScheduleMany()`, `.ThenInclude()`, `.ThenIncludeMany()`, `.Include()`, or `.IncludeMany()`, recovers stuck jobs, and cleans up orphaned manifest groups. It completes before the polling services start.

The **ManifestManagerPollingService** and **JobDispatcherPollingService** are independent `BackgroundService` instances, each with their own configurable polling interval (default: 5 seconds). They communicate via the work queue — ManifestManager writes entries, JobDispatcher reads them. Running independently means JobDispatcher may not see ManifestManager's freshly-queued entries until its next tick, but no work is lost. Both services are safe to run across multiple server instances — see [Multi-Server Concurrency](scheduler/concurrency.md).

The **ManifestManagerTrain** loads enabled manifests, dead-letters any that have exceeded their retry limit, determines which are due for execution (including [dependent manifests](scheduler/dependent-trains.md) whose parent has a newer `LastSuccessfulRun`), and writes them to the work queue. It doesn't enqueue anything directly—it just records intent. In multi-server deployments, a PostgreSQL advisory lock ensures only one server runs the ManifestManager per cycle.

The **JobDispatcherTrain** reads from the work queue, enforces both global and per-group `MaxActiveJobs` limits, creates `Metadata` records, and enqueues to the background task server. This is the single gateway to execution. Everything goes through the work queue first—manifest schedules, `TriggerAsync` calls, dashboard re-runs—so capacity enforcement happens in one place. Each entry is dispatched within its own transaction using `FOR UPDATE SKIP LOCKED`, allowing multiple servers to dispatch concurrently without duplicate execution.

The **TaskServerExecutorTrain** runs on the task server's worker threads for each enqueued job. It loads the Metadata and Manifest, validates the job is still pending, executes the target train via `ITrainBus`, and updates `LastSuccessfulRun` on success. See [Task Server](scheduler/task-server.md) for details on the built-in PostgreSQL implementation.

See [Administrative Trains](scheduler/admin-trains.md) for detailed documentation on each internal train.

## API Reference

For complete method signatures, all parameters, and detailed usage examples for every scheduling function, see the [Scheduler API Reference]({{ site.baseurl }}{% link api-reference/scheduler-api.md %}):

- [Schedule / ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}) — single recurring train
- [ScheduleMany / ScheduleManyAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %}) — batch scheduling with pruning
- [Dependent Scheduling]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %}) — ThenInclude, ThenIncludeMany, Include, IncludeMany, ScheduleDependentAsync
- [Manifest Management]({{ site.baseurl }}{% link api-reference/scheduler-api/manifest-management.md %}) — DisableAsync, EnableAsync, TriggerAsync
- [Scheduling Helpers]({{ site.baseurl }}{% link api-reference/scheduler-api/scheduling-helpers.md %}) — Every, Cron, Schedule record, ManifestOptions

## Sample Project

A working example with the built-in PostgreSQL task server, bulk scheduling, metadata cleanup, and the dashboard is in [`samples/Trax.Samples.Scheduler`](https://github.com/Theauxm/Trax.Core/tree/main/samples/Trax.Samples.Scheduler).
