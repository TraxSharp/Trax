---
layout: default
title: Schedule / ScheduleAsync
description: Reference for Schedule and ScheduleAsync, which upsert one recurring manifest by external id, with ScheduleOptions, manifest-level and group-level settings.
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 3
---

# Schedule / ScheduleAsync

Schedules a single train to run on a recurring basis. `Schedule` is used at startup configuration time; `ScheduleAsync` is used at runtime via `ITraxScheduler`.

Both use **upsert semantics**: if a manifest with the given `externalId` already exists, it is updated; otherwise a new one is created.

## Signatures

### Startup (SchedulerConfigurationBuilder, Recommended)

The input type `TInput` is inferred from the `TTrain` interface at configuration time, so only a single type parameter is needed:

```csharp
public SchedulerConfigurationBuilder Schedule<TTrain>(
    string externalId,
    IManifestProperties input,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class
```

The method resolves `TInput` by reflecting on `TTrain`'s `IServiceTrain<TInput, TOutput>` interface. If the provided `input` doesn't match the expected type, an `InvalidOperationException` is thrown at configuration time. The output type is not constrained: scheduled trains can return any output type, and the output is discarded for background jobs.

### Startup (SchedulerConfigurationBuilder, Explicit Type Parameters)

The legacy three-type-parameter form is still available for backward compatibility:

```csharp
public SchedulerConfigurationBuilder Schedule<TTrain, TInput, TOutput>(
    string externalId,
    TInput input,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

### Runtime (ITraxScheduler)

```csharp
Task<Manifest> ScheduleAsync<TTrain, TInput, TOutput>(
    string externalId,
    TInput input,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

## Type Parameters

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TTrain` | `class` (inferred) / `IServiceTrain<TInput, TOutput>` (explicit) | The train interface type. Can implement `IServiceTrain<TInput, TOutput>` with any output type. The scheduler resolves the concrete implementation via `TrainBus` using the input type. The output is discarded for background jobs. |
| `TInput` | `IManifestProperties` | **Inferred at startup** from `TTrain`'s interface. The input type for the train. Must implement `IManifestProperties` (a marker interface) to enable serialization for scheduled job storage. Only required explicitly in the explicit three-type-param form and the runtime API. |
| `TOutput` | _(none)_ | The output type of the train. Not constrained; any output type is accepted. The output is discarded when the job completes. Only required explicitly in the explicit three-type-param form and the runtime API. |

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `externalId` | `string` | Yes | _(none)_ | A unique identifier for this scheduled job. Used for upsert semantics: if a manifest with this ID exists, it will be updated; otherwise a new one is created. Also used to reference this job in dependent scheduling (`ThenInclude`, `Include`). |
| `input` | `IManifestProperties` (inferred) / `TInput` (explicit) | Yes | _(none)_ | The input data that will be passed to the train on each execution. Serialized and stored in the manifest. With the inferred API, the concrete type is validated against the train's expected input type at configuration time. |
| `schedule` | `Schedule` | Yes | _(none)_ | The schedule definition, either interval-based or cron-based. Cron schedules support both 5-field (minute) and 6-field (second) granularity. Use [Every](/docs/sdk-reference/scheduler-api/scheduling-helpers) or [Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) helpers to create one. Supports `.WithVariance(TimeSpan)` to add random jitter. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options via a fluent builder. Includes manifest-level settings (`Priority`, `Enabled`, `MaxRetries`, `Timeout`), group-level settings (`.Group(...)` with `MaxActiveJobs`, `Priority`, `Enabled`), and batch settings (`PrunePrefix`). See [ScheduleOptions](#scheduleoptions) below. |
| `ct` | `CancellationToken` | No | `default` | Cancellation token (runtime API only). |

## ScheduleOptions

The `ScheduleOptions` fluent builder consolidates all optional scheduling parameters:

### Manifest-level methods

| Method | Description |
|--------|-------------|
| `.Priority(int)` | Dispatch priority (0-31) of every entry queued for this manifest. Higher values dispatched first, within the order set by group priority. Unstated, a new manifest takes 0 and an existing one keeps its value. |
| `.Enabled(bool)` | Whether the manifest is enabled. Unstated, a new manifest is enabled and an existing one keeps its current state, so a runtime disable survives a restart. |
| `.MaxRetries(int)` | Retries after the first run before dead-lettering: the manifest is dead-lettered when its failures within its failure window (`FailureWindow`, or the scheduler's `FailureCountWindow`) exceed it. `0` runs once and dead-letters on the first failure; the default `3` allows four attempts. Unstated, a new manifest takes the scheduler's `DefaultMaxRetries` (3) and an existing one keeps its value, including a runtime change. Throws `ArgumentOutOfRangeException` for a negative value. |
| `.FailureWindow(TimeSpan)` | How far back this manifest's failed runs count toward its retry backoff and `MaxRetries`, in place of the scheduler's `FailureCountWindow`. Stored in whole seconds; throws `ArgumentOutOfRangeException` unless between one second and ten years. Written to an existing manifest only when stated, so a manifest that stops stating it keeps the window it has. |
| `.Timeout(TimeSpan)` | Job execution timeout. Unstated, a new manifest uses the global `DefaultJobTimeout` and an existing one keeps its value. |
| `.OnMisfire(MisfirePolicy)` | Misfire policy for missed runs. Unstated: the scheduler's `DefaultMisfirePolicy` (`FireOnceNow`). `FireOnceNow` fires immediately; `DoNothing` skips and waits for the next natural occurrence. Only applies to Cron and Interval types. |
| `.MisfireThreshold(TimeSpan)` | Grace period before the misfire policy takes effect. Overrides the global `DefaultMisfireThreshold`. |
| `.Variance(TimeSpan)` | Adds random jitter to the schedule. After each successful run, the next execution is delayed by `[0, variance]` seconds. Applies to `Interval` and `Cron` schedules only; on a dependent or one-off manifest it is ignored. If `Schedule.WithVariance()` is also set, the schedule-level value takes precedence. See [Schedule Variance](/docs/scheduler/scheduling-options#schedule-variance). |
| `.Exclude(Exclusion)` | Adds an exclusion window. The manifest is skipped when any exclusion matches the current time. Multiple can be combined. Use `Exclude.DaysOfWeek(...)`, `Exclude.Dates(...)`, `Exclude.DateRange(...)`, or `Exclude.TimeWindow(...)` factories. See [Exclusion Windows](/docs/scheduler/exclusions). |

### Group-level methods

| Method | Description |
|--------|-------------|
| `.Group(string groupId)` | Sets the manifest group name. Defaults to `externalId` when not set. |
| `.Group(string groupId, Action<ManifestGroupOptions>)` | Sets group name and configures group dispatch settings. |
| `.Group(Action<ManifestGroupOptions>)` | Configures group dispatch settings without changing the group name. |

### ManifestGroupOptions

| Method | Description |
|--------|-------------|
| `.MaxActiveJobs(int?)` | Max concurrent active jobs for this group. `null` = no per-group limit. |
| `.Priority(int)` | Group dispatch priority (0-31). Unstated, a new group takes the manifest's priority. |
| `.Enabled(bool)` | Kill switch for the entire group. Unstated, a new group is enabled. |

Each group setting is written only when stated: an existing group keeps any setting its members leave unstated, including a change made from the dashboard. Members of one group that state different values for a setting fail `AddScheduler` at startup. See [What a Restart Rewrites](/docs/scheduler/scheduling-options#what-a-restart-rewrites).

## Returns

- **Startup**: `SchedulerConfigurationBuilder`, for continued fluent chaining.
- **Runtime**: `Task<Manifest>`, the created or updated manifest record.

## Examples

### Startup Configuration (Recommended, Inferred Input Type)

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<ISyncTrain>(
            "sync-daily",
            new SyncInput { Source = "production" },
            Cron.Daily(hour: 3),
            options => options
                .MaxRetries(5)
                .Priority(20)
                .Group("daily-syncs"))
    )
);
```

`AddScheduler` extends the builder `AddMediator` returns, so it comes after `AddEffects` and
`AddMediator`; called earlier, it does not compile. Only the train interface type is specified. The input type (`SyncInput`) is inferred from `ISyncTrain : IServiceTrain<SyncInput, TOutput>` and validated at configuration time. The output type is not constrained. It can be `Unit` or any other type, and the output is discarded for background jobs.

### Runtime Scheduling

```csharp compile
public class MyService(ITraxScheduler scheduler)
{
    public async Task SetupSchedule()
    {
        var manifest = await scheduler.ScheduleAsync<ISyncTrain, SyncInput, Unit>(
            "sync-on-demand",
            new SyncInput { Source = "staging" },
            Every.Hours(1),
            options => options.Priority(15));
    }
}
```

## Schedule Record

The `Schedule` record defines the timing for a scheduled manifest. Create instances via `Every.*`, `Cron.*`, or `Schedule.FromInterval()` / `Schedule.FromCron()`.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `ScheduleType` | `Interval` or `Cron`. |
| `Interval` | `TimeSpan?` | The interval between runs (Interval type only). At least one second: `Schedule.FromInterval` throws `ArgumentOutOfRangeException` for anything shorter. |
| `CronExpression` | `string?` | The cron expression, evaluated in UTC (Cron type only). `Schedule.FromCron` parses it and throws `FormatException` when it is invalid or has no occurrence in the next ten years. A new cron first runs at its first occurrence after it is scheduled. |
| `Variance` | `TimeSpan?` | Optional random jitter added after each successful run. |

### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `WithVariance(TimeSpan variance)` | `Schedule` | Returns a new `Schedule` with the specified variance. The next run after each success is delayed by a random `[0, variance]` duration. A `Schedule` is always `Interval` or `Cron`, so any schedule accepts it. A negative variance throws `InvalidOperationException` when the manifest is scheduled. |

```csharp compile
// Interval with 2-minute jitter
var every5Minutes = Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2));

// Cron with 30-minute jitter
var dailyAt3 = Cron.Daily(3).WithVariance(TimeSpan.FromMinutes(30));
```

## Remarks

- At startup, manifests are not created immediately. They are captured and seeded when the application starts via `SchedulerStartupService`.
- At runtime, `ScheduleAsync` creates/updates the manifest in the database immediately.
- The `externalId` is the primary key for upsert logic. Changing it creates a new manifest rather than updating the existing one.
- If the train type is not registered in the `TrainRegistry` (via `AddMediator`), an `InvalidOperationException` is thrown.
- The group is determined by `.Group(...)` on `ScheduleOptions`. When not specified, it defaults to the `externalId`. Groups are auto-created (upserted by name) during scheduling. Orphaned groups are cleaned up on startup.
- For **one-off jobs** that should run once and auto-disable, use [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management#scheduleonceasync) instead of `ScheduleAsync`. It creates a manifest with `ScheduleType.Once` and needs no `Schedule` object. See [Delayed / One-Off Jobs](/docs/scheduler/delayed-jobs) for details.
