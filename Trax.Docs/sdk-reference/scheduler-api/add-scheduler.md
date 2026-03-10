---
layout: default
title: AddScheduler
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 1
---

# AddScheduler

Adds the Trax.Core scheduler subsystem. Registers `ITraxScheduler`, the background polling service, and all scheduler infrastructure. Provides a `SchedulerConfigurationBuilder` lambda for configuring global options, execution backends, and startup schedules.

## Signature

```csharp
// With configuration
public static TraxBuilderWithMediator AddScheduler(
    this TraxBuilderWithMediator builder,
    Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
)

// Parameterless defaults
public static TraxBuilderWithMediator AddScheduler(
    this TraxBuilderWithMediator builder
)
```

`AddScheduler` is called on `TraxBuilderWithMediator` (the return type of `AddMediator()`), which enforces at compile time that effects and the mediator are configured before the scheduler.

The parameterless overload registers the scheduler with default settings, equivalent to `AddScheduler(scheduler => scheduler)`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder>` | No | Lambda that receives the scheduler builder and returns it after configuring options, execution backends, and schedules. Omit for defaults. |

## Returns

`TraxBuilderWithMediator` — for continued fluent chaining (e.g., adding another `AddScheduler()` call is not typical, but the type allows further configuration).

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseLocalWorkers()
        .ManifestManagerPollingInterval(TimeSpan.FromSeconds(5))
        .JobDispatcherPollingInterval(TimeSpan.FromSeconds(5))
        .MaxActiveJobs(50)
        .DefaultMaxRetries(5)
        .DefaultRetryDelay(TimeSpan.FromMinutes(2))
        .RetryBackoffMultiplier(2.0)
        .MaxRetryDelay(TimeSpan.FromHours(1))
        .DefaultJobTimeout(TimeSpan.FromMinutes(20))
        .DefaultMisfirePolicy(MisfirePolicy.FireOnceNow)
        .DefaultMisfireThreshold(TimeSpan.FromSeconds(60))
        .RecoverStuckJobsOnStartup()
        .DependentPriorityBoost(16)
        .AddMetadataCleanup()
        .Schedule<MyTrain>(
            "my-job",
            new MyInput(),
            Every.Minutes(5),
            options => options
                .Priority(10)
                .Group("my-group"))
    )
);
```

## SchedulerConfigurationBuilder Options

These methods are available on the `SchedulerConfigurationBuilder` passed to the `configure` lambda:

### Execution Backend

| Method | Description |
|--------|-------------|
| [UseLocalWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-local-workers.md %}) | Built-in PostgreSQL local workers (recommended) |
| [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) | Dispatches queued jobs via HTTP POST to a remote endpoint |
| [UseRemoteRun]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %}) | Offloads synchronous `run` execution to a remote endpoint (blocks until complete) |
| [UseHangfire]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-hangfire.md %}) | Configures Hangfire as the execution backend (deprecated) |
| `OverrideSubmitter(Action<IServiceCollection>)` | Registers a custom job submitter implementation |

### Global Options

| Method | Parameter | Default | Description |
|--------|-----------|---------|-------------|
| `PollingInterval(TimeSpan)` | interval | 5 seconds | Shorthand — sets both `ManifestManagerPollingInterval` and `JobDispatcherPollingInterval` to the same value |
| `ManifestManagerPollingInterval(TimeSpan)` | interval | 5 seconds | How often the ManifestManager evaluates manifests and writes to the work queue |
| `JobDispatcherPollingInterval(TimeSpan)` | interval | 2 seconds | How often the JobDispatcher reads from the work queue and dispatches to the job submitter |
| `MaxActiveJobs(int?)` | maxJobs | 10 | Max concurrent active jobs (Pending + InProgress) globally. `null` = unlimited. Per-group limits can also be set from the dashboard on each ManifestGroup |
| `ExcludeFromMaxActiveJobs<TTrain>()` | — | — | Excludes a train type from the MaxActiveJobs count |
| `DefaultMaxRetries(int)` | maxRetries | 3 | Retry attempts before dead-lettering |
| `DefaultRetryDelay(TimeSpan)` | delay | 5 minutes | Base delay between retries |
| `RetryBackoffMultiplier(double)` | multiplier | 2.0 | Exponential backoff multiplier. Set to 1.0 for constant delay |
| `MaxRetryDelay(TimeSpan)` | maxDelay | 1 hour | Caps retry delay to prevent unbounded growth |
| `DefaultJobTimeout(TimeSpan)` | timeout | 20 minutes | Timeout after which a running job is considered stuck |
| `DefaultMisfirePolicy(MisfirePolicy)` | policy | `FireOnceNow` | Default [misfire policy]({{ site.baseurl }}{% link scheduler/scheduling-options.md %}#misfire-policies) for manifests that don't specify one |
| `DefaultMisfireThreshold(TimeSpan)` | threshold | 60 seconds | Grace period before misfire policies take effect. If a manifest is overdue by less than this, it fires normally |
| `RecoverStuckJobsOnStartup(bool)` | recover | `true` | Whether to auto-recover stuck jobs on startup |
| `StalePendingTimeout(TimeSpan)` | timeout | 20 minutes | Timeout after which a Pending job that was never picked up is automatically failed |
| `PruneOrphanedManifests(bool)` | prune | `true` | Whether to [delete manifests]({{ site.baseurl }}{% link scheduler/orphan-manifest-cleanup.md %}) from the database that are no longer defined in the startup configuration. Disable if you create manifests dynamically at runtime via `ITraxScheduler` |
| `DependentPriorityBoost(int)` | boost | 16 | Priority boost added to dependent train work queue entries at dispatch time. Range: 0-31. Ensures dependent trains are dispatched before non-dependent ones by default |

### Startup Schedules

| Method | Description |
|--------|-------------|
| [Schedule]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %}) | Schedules a single recurring train (seeded on startup) |
| [ScheduleMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule-many.md %}) | Batch-schedules manifests from a collection |
| [Then / ThenMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}) | Schedules dependent trains |
| [AddMetadataCleanup]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-metadata-cleanup.md %}) | Enables automatic metadata purging |

## Remarks

- `AddScheduler` requires `AddEffects()` and `AddMediator()` to be called first. This is enforced at compile time -- `AddScheduler` is only available on `TraxBuilderWithMediator`, which is the return type of `AddMediator()`.
- `AddScheduler` requires a data provider (`UsePostgres()` or `UseInMemory()`). If no data provider is configured, `AddScheduler` throws `InvalidOperationException` at build time with a helpful error message showing the required configuration.
- Internal scheduler trains (`ManifestManager`, `InMemoryManifestManager`, `JobDispatcher`, `JobRunner`, `MetadataCleanup`) are automatically excluded from `MaxActiveJobs`.
- With `UseInMemory()`, `JobDispatcherPollingService` and `MetadataCleanupPollingService` are not registered. The `ManifestManagerPollingService` runs an `InMemoryManifestManagerTrain` that dispatches jobs inline via `InMemoryJobSubmitter`.
- Manifests declared via `Schedule`/`ScheduleMany` are not created immediately — they are seeded on application startup by the `SchedulerStartupService`.
- Manifests declared via `Schedule`/`ThenInclude`/`Include` get a ManifestGroup based on their `groupId` parameter (defaults to externalId). Per-group dispatch controls (MaxActiveJobs, Priority, IsEnabled) are configured from the dashboard.
- At build time, the scheduler validates that ManifestGroup dependencies form a DAG (no circular dependencies). If a cycle is detected, `AddScheduler` throws `InvalidOperationException` with the groups involved. See [Dependent Trains — Cycle Detection]({{ site.baseurl }}{% link scheduler/dependent-trains.md %}#cycle-detection).
