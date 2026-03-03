---
layout: default
title: Trax vs Alternatives
nav_order: 9
---

# Trax vs Quartz.NET vs Hangfire

All three run background work in .NET. They solve different problems.

**Quartz.NET** is a time-based job scheduler — a .NET port of the Java Quartz library. Its strength is precise trigger configuration: cron with seconds granularity, calendar exclusions, daily time windows, DST-aware intervals. You define an `IJob`, attach triggers, and Quartz fires them on schedule.

**Hangfire** is a background job processor. Its strength is simplicity — pass a lambda expression, and the method runs in the background with automatic retries, persistence, and a rich monitoring dashboard. Fire-and-forget, delayed, recurring, and continuation jobs are all one-liners.

**Trax** is a workflow orchestration system with scheduling. Its strength is composable, multi-step trains with typed inputs, railway error handling, dependency chains between scheduled jobs, and automatic execution tracking. The scheduler is one layer of a larger effect system, not a standalone component.

## Feature Comparison

### Scheduling

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Cron expressions | 5-6 field (second granularity) | 6-7 field (second granularity) | 5-field via `Cron.*` helpers |
| Simple intervals | `Every.Minutes(5)` | `SimpleTrigger` with repeat count | `TimeSpan` delay |
| Calendar exclusions | Yes — `Exclude.DaysOfWeek()`, `Exclude.Dates()`, `Exclude.DateRange()` | Yes — holidays, weekends, custom calendars | No |
| Daily time windows | Yes — `Exclude.TimeWindow()` (supports midnight crossover) | Yes — "9am-5pm Mon-Fri every hour" | No |
| DST-aware intervals | No | Yes — `PreserveHourOfDayAcrossDaylightSavings` | No |
| Fire-and-forget | `TriggerAsync(externalId)` | `scheduler.TriggerJob(key)` | `BackgroundJob.Enqueue(() => ...)` |
| Delayed one-off | `ScheduleOnceAsync(input, delay)` or `TriggerAsync(id, delay)` | Trigger with future start time | `BackgroundJob.Schedule(delay)` |
| Misfire policies | Implicit (run if overdue) | 6+ explicit policies per trigger type | N/A (queue-based) |

Trax now supports second-granularity cron via 6-field expressions (e.g., `*/15 * * * * *` for every 15 seconds), closing the gap with Quartz.NET for most sub-minute scheduling use cases. Trax also supports calendar exclusions (days of week, specific dates, date ranges, and daily time windows). Quartz.NET's 7-field cron (with year) and DST-aware triggers are not supported by Trax.

### Job Composition

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Unit of work | `Step` — typed, composable | `IJob` — single `Execute` method | Any public method |
| Multi-step composition | `.Chain<StepA>().Chain<StepB>()` with railway error handling | None built-in | None built-in |
| Error propagation | Either monad — failures short-circuit the chain | `JobExecutionException` thrown from `Execute` | Exception causes `FailedState` |
| Type safety | Strong — `IServiceTrain<TInput, TOutput>`, `IManifestProperties` | Weak — `JobDataMap` (string-keyed dictionary) | Strong — compile-time lambda expressions |
| Job dependencies | First-class: `Include()`, `ThenInclude()` with DAG validation | Manual via listeners or job code | `ContinueJobWith(parentId, ...)` |
| Dormant dependents | Yes — parent activates children at runtime with custom input | No | No |
| Dependency validation | Startup-time DAG cycle detection | No | No |

This is where the three diverge most. A Quartz or Hangfire job is a single unit — complex logic lives in one `Execute` method or one lambda. A Trax train splits that logic into discrete steps, each with its own class, dependencies, and tests. The chain short-circuits on the first failure, and the railway pattern makes error paths explicit rather than relying on try/catch.

The dependency system is unique to Trax. A manifest can declare that it depends on another manifest. When the parent completes, the child fires automatically. Dormant dependents go further — the parent decides at runtime which children to activate and with what input. Hangfire has continuations, but they're linear (A then B) and don't support DAG topologies or runtime activation.

### Persistence and Execution Tracking

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Database backends | PostgreSQL, InMemory | RAM, SQL Server, PostgreSQL, MySQL, Oracle, SQLite | SQL Server (core), Redis/PostgreSQL/MongoDB (community) |
| Execution history | Built-in — every run records input, output, timing, exceptions | Via plugins (`LoggingJobHistoryPlugin`) | Built-in — full state transition history |
| Dead-lettering | Built-in — `dead_letters` table with `AwaitingIntervention` status | No | No (failed jobs stay in `FailedState`) |
| Metadata per execution | Automatic — input JSON, output JSON, duration, stack trace | Manual — `JobDataMap` for custom data | Automatic — state data, exception details |

Trax records more per execution than either alternative. Every train run automatically captures serialized input, output, execution time, and full exception details in a `Metadata` row — this comes from the effect system's `ServiceTrain` base class, not from the scheduler. Quartz requires opting in via history plugins, and while Hangfire tracks state transitions well, it doesn't serialize job inputs and outputs into queryable columns.

Trax is PostgreSQL-only for production persistence. Quartz supports six database backends. Hangfire's core targets SQL Server with community packages for Redis and others.

### Distributed Execution

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Multi-server support | Yes | Yes | Yes |
| Coordination mechanism | PostgreSQL advisory locks + `FOR UPDATE SKIP LOCKED` | Database row locks + heartbeat check-in | Distributed locks + atomic queue fetch |
| Capacity control | Global `MaxActiveJobs` + per-group limits | Thread pool size | Worker count per server |
| Group-level concurrency | Yes — `ManifestGroup` with independent caps | No | No |
| Leader election | Advisory lock on ManifestManager (one evaluator per cycle) | Heartbeat-based failover | Distributed lock per critical operation |

All three support running across multiple servers. Trax's two-stage architecture — ManifestManager evaluates schedules under advisory lock, JobDispatcher claims work with `SKIP LOCKED` — keeps scheduling decisions centralized while allowing concurrent dispatch. ManifestGroups add per-group concurrency caps, so a batch of 1,000 table-sync jobs won't starve your critical billing jobs.

### Retry and Failure Handling

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Automatic retry | Yes — configurable max retries | No — job must handle its own retry | Yes — 10 retries by default |
| Backoff strategy | Exponential (configurable multiplier and cap) | N/A | Exponential (attempt⁴ + 15 + random) |
| Dead-letter queue | Yes — after max retries | No | No (jobs stay in `FailedState`) |
| Stuck job recovery | Yes — `RecoverStuckJobsOnStartup` + configurable timeout | Yes — `RequestsRecovery` flag | Yes — server watchdog requeues |

Trax and Hangfire both handle retries automatically; Quartz leaves it to the job. Trax adds dead-lettering on top — after `MaxRetries` failures, the job moves to a dead-letter table and stops retrying. This separates "transient failure, will recover" from "needs human attention" without manual intervention.

### Dashboard and Monitoring

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Dashboard | Blazor Server + Radzen at `/trax` | Blazor Server at `/quartz` | Built-in middleware at `/hangfire` |
| REST / GraphQL API | Both — `AddTraxRestApi()` + `AddTraxGraphQL()` | No built-in API | REST API via community extensions |
| Manual job actions | Dashboard-triggered runs with **custom inputs** | Pause/resume, manual trigger | Requeue, delete, reschedule |
| Exception inspection | Expandable stack traces with syntax highlighting + copy button | Via history plugins | Expandable stack traces |
| Real-time statistics | Throughput/min, queue depth, success rate, state timeline | Job/trigger counts, health checks | Succeeded/failed rates, queue depth |
| Run with new inputs | Yes — form builder or JSON for any train | No | No (requeue with original only) |

Hangfire's dashboard is its flagship feature — battle-tested, rich, and purpose-built for job monitoring. Quartz recently added a Blazor dashboard. Trax's dashboard includes state transition timelines, syntax-highlighted exception viewers, real-time throughput metrics, and the ability to run any train with custom inputs from the UI — a capability unique to Trax.

### Developer Experience

| Feature | Trax | Quartz.NET | Hangfire |
|---|---|---|---|
| Lines to schedule "hello world" | ~50 (train + step + manifest) | ~30 (job class + scheduler config) | ~5 (`BackgroundJob.Enqueue(...)`) |
| DI integration | Full — constructor injection in trains and steps | Full — scoped services via `IJobFactory` | Full — scoped services via `JobActivator` |
| Testing | In-memory data provider, steps testable in isolation | In-memory store, manual trigger | Service replacement via DI |
| Learning curve | Highest — trains, steps, effects, manifests, mediator | Medium — standard scheduler concepts | Lowest — "call a method in the background" |

Hangfire wins on ceremony. You can go from zero to a running background job in five lines. Trax requires understanding trains, steps, the effect system, and manifest registration before your first scheduled job runs. That structure pays off in larger systems — but it's real overhead for simple tasks.

## When to Choose Trax

**Your scheduled work has internal structure.** If a job has distinct phases — extract, transform, validate, load — splitting them into steps makes each phase independently testable, reusable, and visible in execution metadata. A monolithic `Execute()` method hides that structure.

**Jobs depend on other jobs.** The dependency system with `Include()`, `ThenInclude()`, and dormant dependents handles DAG topologies that neither Quartz nor Hangfire supports natively. If "job B should only run after job A succeeds" is a core requirement, Trax models it directly rather than bolting it on with listeners or continuations.

**You need execution audit trails.** Every train run automatically records input, output, timing, and failure details. This is structural — it comes from the `ServiceTrain` base class, not from opt-in plugins or configuration. If compliance or debugging requires knowing exactly what a job received, what it produced, and how long it took, the data is already there.

**You're doing controlled bulk orchestration.** Scheduling 1,000 table-sync manifests with `ScheduleMany`, grouping them for concurrency control, and letting the dispatcher enforce capacity limits per group is a first-class workflow. ManifestGroups prevent starvation across independent workloads.

**You want dead-lettering.** The separation between "will retry" and "needs human attention" matters for operations. After max retries, the job moves to a dead-letter table and stops consuming retry cycles. Operators can review, fix the root cause, and re-trigger.

**You're already using the Trax effect system.** If your application already uses `ServiceTrain` for workflow composition, the scheduler is a natural extension — same base classes, same step model, same metadata tracking. The scheduler adds a timetable to trains you've already built.

## When NOT to Choose Trax

**You need a simple background job processor.** If the job is "send this email in 5 minutes" or "resize this image asynchronously," Hangfire does it in one line. Trax now supports delayed one-off jobs via `ScheduleOnceAsync`, but the train/step/manifest model still adds ceremony compared to Hangfire's lambda-based approach for simple fire-and-forget work with no internal structure.

**You need DST-aware scheduling or 7-field cron with year.** Trax supports second-granularity cron via 6-field expressions and calendar exclusions, but DST-aware interval handling and Quartz.NET's 7-field (year) format are not supported.

**You're not on PostgreSQL.** Trax's distributed coordination relies on PostgreSQL advisory locks and `FOR UPDATE SKIP LOCKED`. There is no SQL Server, MySQL, or Redis backend. If your infrastructure is on a different database, Quartz.NET or Hangfire will work; Trax won't.

**You want the lowest learning curve.** Trax requires understanding trains, steps, the effect system, railway programming, manifests, and the mediator pattern before writing your first scheduled job. Hangfire requires understanding `BackgroundJob.Enqueue()`. For teams that need to onboard quickly or for projects where background work is a small part of the system, the abstraction cost isn't justified.

**You need built-in access control on the dashboard.** Hangfire's dashboard includes middleware-based authorization out of the box. Trax's dashboard does not currently provide built-in authentication or authorization — you'd need to add it via ASP.NET Core middleware. Both dashboards now offer comparable monitoring features (real-time statistics, exception inspection, manual actions), but Hangfire has the edge on access control.

**You want a standalone scheduler.** Trax.Scheduler doesn't exist in isolation — it depends on Trax.Effect, Trax.Mediator, and Trax.Core. You're adopting a framework, not plugging in a library. Quartz.NET and Hangfire are self-contained: add the NuGet package, configure storage, schedule jobs.

## They're Not Mutually Exclusive

Trax can use Hangfire as its background task server via the [Trax.Scheduler.Hangfire](https://www.nuget.org/packages/Trax.Scheduler.Hangfire/) package. In this configuration, Trax handles manifest management, dependency resolution, and capacity control while Hangfire handles the actual job queuing and worker management. This gives you Hangfire's mature processing infrastructure with Trax's orchestration layer on top.

```csharp
services.AddTraxEffects(options => options
    .AddServiceTrainBus(assemblies)
    .AddPostgresEffect(connectionString)
    .AddScheduler(scheduler => scheduler
        .UseHangfire()
        .Schedule<ISyncTrain, SyncInput>("sync", new SyncInput(), Every.Hours(1))));
```
