---
layout: default
title: Health & Liveness
parent: Scheduling
nav_order: 10
---

# Scheduler Health & Liveness

A TCP-port or `200 OK` probe stays green on a scheduler that is up but dispatching nothing. If the JobDispatcher wedges (a bad query, an exhausted connection pool, a stuck migration), the process keeps answering health checks while no work moves. The container is never replaced, and the outage is silent.

The scheduler-liveness health check closes that gap. The JobDispatcher stamps a monitor after every successful polling cycle, and the health check reports unhealthy when the last stamp is older than a threshold. Wire it into your container or load-balancer probe so a wedged scheduler is restarted instead of idling.

## Wiring the Health Check

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<ISyncTrain, SyncInput>(ScheduledJob.Sync, input, Every.Seconds(30))
    )
);

builder.Services.AddHealthChecks().AddTraxSchedulerLiveness();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

Point your ECS/Kubernetes/ALB liveness probe at `/health`. When the dispatcher stops completing cycles, the endpoint returns 503 and the orchestrator replaces the task.

## How It Works

The scheduler registers an `ISchedulerLivenessMonitor` singleton on startup. `JobDispatcherPollingService` calls `RecordDispatchCycle()` after each successful `train.Run`, including no-op polls where the work queue was empty (a no-op cycle still proves the poll loop and database round-trip work). A failed cycle does not stamp, so the timestamp goes stale and the check flips unhealthy.

Before the first cycle completes, the check measures from startup time instead. A cold start stays healthy within the grace window, but a scheduler that never dispatches still trips once startup is older than the threshold.

## Options

`AddTraxSchedulerLiveness()` takes the following:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `name` | `scheduler-liveness` | The health check name |
| `threshold` | see below | How long the dispatcher may go without completing a cycle before reporting unhealthy |
| `failureStatus` | `Unhealthy` | The status reported when stale (`Degraded` if you want to alert without failing the probe) |
| `tags` | none | Tags for filtering health checks |

When `threshold` is null, the check uses `SchedulerConfiguration.SchedulerLivenessThreshold` (set it on the builder with `.SchedulerLivenessThreshold(...)`), falling back to `max(JobDispatcherPollingInterval * 10, 30s)`. The floor keeps a fast poll interval from producing a flappy check.

```csharp
.AddScheduler(scheduler => scheduler
    .JobDispatcherPollingInterval(TimeSpan.FromSeconds(2))
    .SchedulerLivenessThreshold(TimeSpan.FromSeconds(20))
)
```

The check exposes the last dispatch time, the current age, and the threshold in its `data` payload for dashboards and logs.

## SDK Reference

> [AddTraxSchedulerLiveness](/docs/sdk-reference/scheduler-api/add-trax-scheduler-liveness) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
