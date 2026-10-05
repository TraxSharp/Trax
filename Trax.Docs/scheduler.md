---
layout: default
title: Scheduling
description: "Trax.Scheduler overview: manifests, execution metadata, dead letters, delayed and dependent jobs, manifest groups and how the scheduler is put together."
nav_order: 6
has_children: true
section: Packages
---

# Scheduling

Trax.Scheduler adds timetable management to trains. Define a manifest (like a shipping manifest listing what train to dispatch, when, and how many retries) and the scheduler handles execution, retries, and dead-lettering.

This isn't a traditional cron scheduler. It supports cron expressions, but its design goal is controlled bulk job orchestration, for example, where you need visibility into every execution attempt.

## When to Use the Scheduler

A hosted service with a timer works fine for simple recurring tasks. The Scheduler is for when you need the audit trail: every execution recorded with inputs, outputs, timing, and failure details. Failed jobs retry automatically. Jobs that fail too many times go to a dead letter queue for manual review.

## Core Concepts

### Manifest = Job Definition

A `Manifest` is the scheduling equivalent of a shipping manifest. It describes what train to run, when to dispatch it, how to handle failures, and what cargo (input) to load. The `ITraxScheduler` handles the boilerplate, so you don't need to worry about assembly-qualified names or JSON serialization:

```csharp
using Trax.Core.Functional;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;

await scheduler.ScheduleAsync<ISyncCustomersTrain, SyncCustomersInput, Unit>(
    "sync-customers-us-east",
    new SyncCustomersInput { Region = "us-east", BatchSize = 500 },
    Every.Hours(6),
    opts => opts.MaxRetries(3));

// For bulk scheduling from a collection, use ScheduleManyAsync:
await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, string>(
    tables,
    table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    Every.Minutes(5));
```

The scheduler creates the manifest, resolves the correct type names, and serializes the input automatically. Every call is an upsert, safe to run on every startup without duplicating jobs.

### Metadata = Execution Record

Each time a manifest runs, it creates a new `Metadata` record. These are **immutable**: retries create new rows, never mutate existing ones. This produces a complete audit trail:

```
Manifest: "sync-customers-us-east"
├── Metadata #1: Completed at 10:00:00
├── Metadata #2: Failed at 10:05:00 (timeout)
├── Metadata #3: Failed at 10:10:00 (timeout)
├── Metadata #4: Failed at 10:15:00 (timeout) -> Dead-lettered
```

### Dead Letter = Failed Beyond Retry

Like a lost shipment in a postal system, when a job fails more times than `MaxRetries`, it moves to the dead letter queue. Dead letters require manual intervention. The scheduler won't automatically retry them.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Dead Letter                               │
├─────────────────────────────────────────────────────────────────┤
│ Status: AwaitingIntervention                                    │
│ Reason: Max retries exceeded: (4) failures > (3) max retries    │
│ DeadLetteredAt: 2026-02-10 10:15:00                            │
└─────────────────────────────────────────────────────────────────┘
```

Operators can retry (which creates a new execution) or acknowledge (mark as handled without retry).

### Delayed / One-Off Jobs

Not all work is recurring. `TriggerAsync(externalId, delay)` queues a delayed execution of an existing manifest. `ScheduleOnceAsync` creates a new manifest with `ScheduleType.Once` that fires after a delay and auto-disables on success. See [Delayed / One-Off Jobs](/docs/scheduler/delayed-jobs).

```csharp
// Delayed trigger of an existing manifest
await scheduler.TriggerAsync("sync-customers", TimeSpan.FromMinutes(30));

// One-off job: runs once after 24 hours, then auto-disables
await scheduler.ScheduleOnceAsync<ISendReminderTrain, SendReminderInput, Unit>(
    new SendReminderInput { UserId = userId },
    TimeSpan.FromHours(24));
```

### Dependent Manifests

A manifest can depend on another manifest: one train's arrival triggers another's departure. Instead of running on a timer, it fires when its parent's `LastSuccessfulRun` advances past the dependent's own. This is how you build ETL chains, post-processing junctions, or any train that should only run after another succeeds. See [Dependent Trains](/docs/scheduler/dependent-trains).

```csharp
scheduler
    .Schedule<IExtractTrain>(
        "extract", new ExtractInput(), Every.Hours(1))
    .Include<ILoadTrain>(
        "load", new LoadInput());
```

### Manifest Groups

Every manifest belongs to a `ManifestGroup`. Groups provide per-group dispatch controls (`MaxActiveJobs`, `Priority`, and `IsEnabled`), configurable from the dashboard. When a group hits its concurrency cap, the dispatcher skips it and continues processing other groups, preventing starvation.

```csharp
scheduler
    .Schedule<ISyncTrain>(
        "sync-data", new SyncInput(), Every.Hours(1),
        opts => opts.Group("data-sync"))
    .Include<ILoadTrain>(
        "load-data", new LoadInput(),
        opts => opts.Group("data-sync"));
```

When no group is set, it defaults to the manifest's `externalId`. See [Scheduling Options](/docs/scheduler/scheduling-options#per-group-dispatch-controls).

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
│                  JobRunnerTrain                                │
│            (runs on LocalWorkerService workers)                   │
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

The **SchedulerStartupService** is an `IHostedService` that runs once on startup. It seeds any manifests configured via `.Schedule()`, `.ScheduleMany()`, `.ScheduleOnce()`, `.ThenInclude()`, `.ThenIncludeMany()`, `.Include()`, or `.IncludeMany()`, recovers stuck jobs, and cleans up orphaned manifest groups. It completes before the polling services start.

The **ManifestManagerPollingService** and **JobDispatcherPollingService** are independent `BackgroundService` instances, each with their own configurable polling interval (defaults: 5 seconds for the ManifestManager, 2 seconds for the JobDispatcher). They communicate via the work queue: ManifestManager writes entries, JobDispatcher reads them. Running independently means JobDispatcher may not see ManifestManager's freshly-queued entries until its next tick, but no work is lost. Both services are safe to run across multiple server instances. See [Multi-Server Concurrency](/docs/scheduler/concurrency).

The **ManifestManagerTrain** loads enabled manifests, dead-letters any that have exceeded their retry limit, determines which are due for execution (including [dependent manifests](/docs/scheduler/dependent-trains) whose parent succeeded after the dependent's latest run started), and writes them to the work queue. It doesn't enqueue anything directly; it just records intent. In multi-server deployments, a PostgreSQL advisory lock guarantees only one server runs the ManifestManager per cycle.

The **JobDispatcherTrain** reads from the work queue, enforces both global and per-group `MaxActiveJobs` limits, creates `Metadata` records, and enqueues to the job submitter. This is the single gateway to execution. Everything goes through the work queue first (manifest schedules, `TriggerAsync` calls, dashboard re-runs), so capacity enforcement happens in one place. Each entry is dispatched within its own transaction using `FOR UPDATE SKIP LOCKED`, allowing multiple servers to dispatch concurrently without duplicate execution.

The **JobRunnerTrain** runs on the local worker threads for each enqueued job. It loads the Metadata and Manifest, validates the job is still pending, executes the target train via `ITrainBus`, and updates `LastSuccessfulRun` on success. See [Job Submission](/docs/scheduler/job-submission) for details on the built-in PostgreSQL implementation.

See [Administrative Trains](/docs/scheduler/admin-trains) for detailed documentation on each internal train.

## SDK Reference

> [Schedule / ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule) | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | [ThenInclude / Include](/docs/sdk-reference/scheduler-api/dependent-scheduling) | [TriggerAsync / ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers)

## Sample Project

The [Scheduling sample](/docs/samples/scheduling) is a complete scheduler host on Postgres with
the built-in local workers: interval, cron, one-off, dependent and dormant dependent manifests, a
train that retries with backoff and is dead-lettered, the GraphQL operations surface to requeue it,
metadata cleanup and the dashboard. Its page has the whole `Program.cs` and the startup errors each
missing piece produces.

## Next Layer

When you need a programmatic interface for external consumers (queuing jobs, running trains on demand, and querying state over HTTP), add [Trax.Api](/docs/api).
