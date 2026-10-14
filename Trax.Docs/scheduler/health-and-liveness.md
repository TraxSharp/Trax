---
title: Restart a wedged scheduler with a liveness probe
description: "How to wire the scheduler liveness health check into a container or load-balancer probe, so a scheduler that stops dispatching is restarted."
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/SchedulerLiveness
type: how-to
area: scheduling
nav_order: 4
---

# Restart a wedged scheduler with a liveness probe

A TCP-port or `200 OK` probe stays green on a scheduler that is up but dispatching nothing. If the JobDispatcher wedges (a bad query, an exhausted connection pool, a stuck migration), the process keeps answering health checks while no work moves. The container is never replaced, and the outage is silent.

The scheduler-liveness health check closes that gap. The JobDispatcher stamps a monitor after every successful polling cycle, and the health check reports unhealthy when the last stamp is older than a threshold. Wire it into your container or load-balancer probe so a wedged scheduler is restarted instead of idling.

## 1. Register the check and map the endpoint

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("TraxDatabase")!;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<ISyncTrain>("sync", new SyncInput(), Every.Seconds(30))
        )
);

builder.Services.AddHealthChecks().AddTraxSchedulerLiveness();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

## 2. Point the probe at it

Point your ECS, Kubernetes or ALB liveness probe at `/health`. When the dispatcher stops completing cycles, the endpoint returns 503 and the orchestrator replaces the task.

## 3. Set the threshold, if the default does not fit

The check reports unhealthy once the dispatcher has gone longer than its threshold without
completing a cycle: `max(JobDispatcherPollingInterval * 10, 30s)` unless you set
`SchedulerLivenessThreshold` on the scheduler or pass `threshold` to `AddTraxSchedulerLiveness`.
Pass `failureStatus: HealthStatus.Degraded` to alert without failing the probe. The parameters are
on [AddTraxSchedulerLiveness](/docs/sdk-reference/scheduler-api/add-trax-scheduler-liveness).

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(2))
                .SchedulerLivenessThreshold(TimeSpan.FromSeconds(20))
        )
);
```

The check's `data` payload carries the last dispatch time, the current age and the threshold, for
dashboards and logs.

## How It Works

The scheduler registers an `ISchedulerLivenessMonitor` singleton on startup. The JobDispatcher's polling loop stamps it after each successful `train.Run`, including no-op polls where the work queue was empty (a no-op cycle still proves the poll loop and database round-trip work). A failed cycle does not stamp, so the timestamp goes stale and the check flips unhealthy.

`ISchedulerLivenessMonitor` is read-only: `StartedAt` and `LastDispatchCompletedAt`. Only the dispatcher can record a cycle, so nothing else in the process can report the scheduler alive. Resolve the interface to read the timestamps. Registering your own implementation in its place does not work: the dispatcher keeps stamping the built-in monitor, and the health check reads yours.

Before the first cycle completes, the check measures from startup time instead. A cold start stays healthy within the grace window, but a scheduler that never dispatches still trips once startup is older than the threshold.

The dispatcher also stamps the start of each cycle. A cycle still running counts from when it began, so a slow synchronous dispatch is not mistaken for a wedged scheduler until the cycle itself has run past the threshold. That applies only when the previous cycle succeeded: a cycle that follows a failed one proves nothing until it completes, so a dispatcher that keeps failing stays unhealthy.

The check reports healthy when nothing on the host is meant to dispatch, since restarting the process would not change that:

- **Dispatch is paused.** An operator turned `jobDispatcherEnabled` off from the dashboard or the `updateScheduler` mutation. The check reports healthy with the description "JobDispatcher is paused.", and the paused loop keeps stamping, so turning dispatch back on does not start from a stale timestamp.
- **The host runs no JobDispatcher.** A scheduler on the InMemory provider registers none (its ManifestManager dispatches inline).

## SDK Reference

> [AddTraxSchedulerLiveness](/docs/sdk-reference/scheduler-api/add-trax-scheduler-liveness) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
