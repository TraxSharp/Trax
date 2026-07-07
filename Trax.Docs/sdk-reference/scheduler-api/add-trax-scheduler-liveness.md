---
layout: default
title: AddTraxSchedulerLiveness
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 12
---

# AddTraxSchedulerLiveness

Registers an ASP.NET Core health check that reports unhealthy when the JobDispatcher has not completed a polling cycle within a threshold. Unlike a process or port probe, this catches a scheduler that is up but dispatching nothing.

## Signature

```csharp
public static IHealthChecksBuilder AddTraxSchedulerLiveness(
    this IHealthChecksBuilder builder,
    string name = "scheduler-liveness",
    TimeSpan? threshold = null,
    HealthStatus failureStatus = HealthStatus.Unhealthy,
    IEnumerable<string>? tags = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | No | `scheduler-liveness` | The health check name |
| `threshold` | `TimeSpan?` | No | `null` | Staleness threshold. When null, uses `SchedulerConfiguration.SchedulerLivenessThreshold`, falling back to `max(JobDispatcherPollingInterval * 10, 30s)` |
| `failureStatus` | `HealthStatus` | No | `Unhealthy` | The status reported when stale |
| `tags` | `IEnumerable<string>?` | No | `null` | Tags for filtering health checks |

## Returns

`IHealthChecksBuilder`, for continued fluent chaining.

## Example

```csharp
builder.Services.AddHealthChecks().AddTraxSchedulerLiveness();

var app = builder.Build();
app.MapHealthChecks("/health");
```

With a custom threshold and a degraded (non-failing) status:

```csharp
builder.Services
    .AddHealthChecks()
    .AddTraxSchedulerLiveness(
        threshold: TimeSpan.FromSeconds(20),
        failureStatus: HealthStatus.Degraded,
        tags: ["scheduler"]
    );
```

## Remarks

- The scheduler registers the underlying `ISchedulerLivenessMonitor` automatically. `JobDispatcherPollingService` stamps it after each successful cycle, including empty no-op polls.
- Before the first cycle completes, the check measures from scheduler startup, so a cold start is healthy within the grace window but a scheduler that never dispatches still trips.
- The check writes `lastDispatchCompletedAt`, `ageSeconds`, and `thresholdSeconds` into its result `data`.
- The threshold can also be set on the scheduler builder with `.SchedulerLivenessThreshold(TimeSpan)`; the `threshold` argument here overrides it.

## See Also

- [Scheduler Health & Liveness](/docs/scheduler/health-and-liveness)
- [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
