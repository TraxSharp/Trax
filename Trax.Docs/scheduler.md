---
title: Scheduling
description: "Trax.Scheduler overview: manifests, execution metadata, dead letters, delayed and dependent jobs, manifest groups and how the scheduler is put together."
source:
  - Trax.Scheduler/src/Trax.Scheduler
type: explanation
area: scheduling
nav_order: 1
---

# Scheduling

Trax.Scheduler adds timetable management to trains. Define a manifest (like a shipping manifest listing what train to dispatch, when, and how many retries) and the scheduler handles execution, retries, and dead-lettering.

This isn't a traditional cron scheduler. It supports cron expressions, but its design goal is controlled bulk job orchestration, for example, where you need visibility into every execution attempt.

## When to Use the Scheduler

A hosted service with a timer works fine for simple recurring tasks. The Scheduler is for when you need the audit trail: every execution recorded with inputs, outputs, timing, and failure details. Failed jobs retry automatically. Jobs that fail too many times go to a dead letter queue for manual review.

## Core Concepts

### Manifest = Job Definition

A `Manifest` is the scheduling equivalent of a shipping manifest. It describes what train to run, when to dispatch it, how to handle failures, and what cargo (input) to load. `ITraxScheduler` writes it for you, resolving the type names and serializing the input:

```csharp compile
await scheduler.ScheduleAsync<ISyncCustomersTrain, SyncCustomersInput, Unit>(
    "sync-customers-us-east",
    new SyncCustomersInput { Region = "us-east", BatchSize = 500 },
    Every.Hours(6),
    opts => opts.MaxRetries(3)
);

// For bulk scheduling from a collection, use ScheduleManyAsync:
string[] tables = ["customers", "orders"];

await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, string>(
    tables,
    table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    Every.Minutes(5)
);
```

Every call is an upsert by external id, so it is safe to run on every startup without duplicating jobs.

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

Operators can requeue (which creates a new execution) or acknowledge (mark as handled without retry). See [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup), and [How retries work](/docs/scheduler/how-retries-work) for what happens before a job gets there.

### Delayed / One-Off Jobs

Not all work is recurring. `TriggerAsync(externalId, delay)` queues a delayed execution of an existing manifest. `ScheduleOnceAsync` creates a new manifest with `ScheduleType.Once` that fires after a delay and disables itself on success. See [Run a job once, later](/docs/scheduler/delayed-jobs).

```csharp compile
// Delayed trigger of an existing manifest
await scheduler.TriggerAsync("sync-customers", TimeSpan.FromMinutes(30));

// One-off job: runs once after 24 hours, then disables itself
await scheduler.ScheduleOnceAsync<ISendReminderTrain, SendReminderInput, Unit>(
    new SendReminderInput { UserId = "user-42" },
    TimeSpan.FromHours(24)
);
```

### Dependent Manifests

A manifest can depend on another manifest: one train's arrival triggers another's departure. Instead of running on a timer, it fires when its parent succeeds after the dependent's own latest run started. This is how you build ETL chains, post-processing steps, or any train that should only run after another succeeds. See [Dependent Trains](/docs/scheduler/dependent-trains).

### Manifest Groups

Every manifest belongs to a `ManifestGroup`. Groups provide per-group dispatch controls (`MaxActiveJobs`, `Priority`, and `IsEnabled`), configurable from code and from the dashboard. When a group hits its concurrency cap, the dispatcher skips it and continues with other groups, so one busy group does not starve the rest.

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler
                .Schedule<IExtractTrain>(
                    "extract-data",
                    new ExtractInput(),
                    Every.Hours(1),
                    opts => opts.Group("data-sync")
                )
                .Include<ILoadTrain>("load-data", new LoadInput(), opts => opts.Group("data-sync"))
        )
);
```

When no group is set, it defaults to the manifest's `externalId`. See [Per-Group Dispatch Controls](/docs/scheduler/scheduling-options#per-group-dispatch-controls).

## How it fits together

A scheduler host runs a handful of hosted services. On startup one seeds the manifests declared in code and recovers runs a crash left behind. After that, two loops run on their own intervals: the ManifestManager decides which manifests are due and writes each to the work queue, and the JobDispatcher reads the queue, applies the capacity limits and hands each run to a job submitter, which runs it on a worker through the JobRunner. Everything that starts a run (a schedule, a trigger, a dead-letter requeue, a dashboard re-run) goes through the work queue, so capacity is enforced in one place.

[Administrative Trains](/docs/scheduler/admin-trains) describes each service and train, [How jobs reach workers](/docs/scheduler/job-submission) the job submitters, and [Multi-Server Concurrency](/docs/scheduler/concurrency) how it stays safe across hosts.

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
