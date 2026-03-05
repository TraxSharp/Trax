---
layout: default
title: Schedule / ScheduleAsync
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 3
---

# Schedule / ScheduleAsync

Schedules a single train to run on a recurring basis. `Schedule` is used at startup configuration time; `ScheduleAsync` is used at runtime via `ITraxScheduler`.

Both use **upsert semantics** — if a manifest with the given `externalId` already exists, it is updated; otherwise a new one is created.

## Signatures

### Startup (SchedulerConfigurationBuilder) — Recommended

The input type `TInput` is inferred from the `TTrain` interface at configuration time — only a single type parameter is needed:

```csharp
public SchedulerConfigurationBuilder Schedule<TTrain>(
    string externalId,
    IManifestProperties input,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class
```

The method resolves `TInput` by reflecting on `TTrain`'s `IServiceTrain<TInput, TOutput>` interface. If the provided `input` doesn't match the expected type, an `InvalidOperationException` is thrown at configuration time. The output type is not constrained — scheduled trains can return any output type, and the output is discarded for background jobs.

### Startup (SchedulerConfigurationBuilder) — Explicit Type Parameters

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
| `TOutput` | — | The output type of the train. Not constrained — any output type is accepted. The output is discarded when the job completes. Only required explicitly in the explicit three-type-param form and the runtime API. |

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `externalId` | `string` | Yes | — | A unique identifier for this scheduled job. Used for upsert semantics — if a manifest with this ID exists, it will be updated; otherwise a new one is created. Also used to reference this job in dependent scheduling (`ThenInclude`, `Include`). |
| `input` | `IManifestProperties` (inferred) / `TInput` (explicit) | Yes | — | The input data that will be passed to the train on each execution. Serialized and stored in the manifest. With the inferred API, the concrete type is validated against the train's expected input type at configuration time. |
| `schedule` | `Schedule` | Yes | — | The schedule definition — either interval-based or cron-based. Cron schedules support both 5-field (minute) and 6-field (second) granularity. Use [Every]({{ site.baseurl }}{% link sdk-reference/scheduler-api/scheduling-helpers.md %}) or [Cron]({{ site.baseurl }}{% link sdk-reference/scheduler-api/scheduling-helpers.md %}) helpers to create one. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options via a fluent builder. Includes manifest-level settings (`Priority`, `Enabled`, `MaxRetries`, `Timeout`), group-level settings (`.Group(...)` with `MaxActiveJobs`, `Priority`, `Enabled`), and batch settings (`PrunePrefix`). See [ScheduleOptions](#scheduleoptions) below. |
| `ct` | `CancellationToken` | No | `default` | Cancellation token (runtime API only). |

## ScheduleOptions

The `ScheduleOptions` fluent builder consolidates all optional scheduling parameters:

### Manifest-level methods

| Method | Description |
|--------|-------------|
| `.Priority(int)` | Dispatch priority (0-31). Higher values dispatched first. |
| `.Enabled(bool)` | Whether the manifest is enabled. Default: `true`. |
| `.MaxRetries(int)` | Max retry attempts before dead-lettering. Default: `3`. |
| `.Timeout(TimeSpan)` | Job execution timeout. `null` uses global default. |
| `.OnMisfire(MisfirePolicy)` | Misfire policy for missed runs. `FireOnceNow` (default) fires immediately; `DoNothing` skips and waits for the next natural occurrence. Only applies to Cron and Interval types. |
| `.MisfireThreshold(TimeSpan)` | Grace period before the misfire policy takes effect. Overrides the global `DefaultMisfireThreshold`. |
| `.Exclude(Exclusion)` | Adds an exclusion window. The manifest is skipped when any exclusion matches the current time. Multiple can be combined. Use `Exclude.DaysOfWeek(...)`, `Exclude.Dates(...)`, `Exclude.DateRange(...)`, or `Exclude.TimeWindow(...)` factories. See [Exclusion Windows]({{ site.baseurl }}{% link scheduler/exclusions.md %}). |

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
| `.Priority(int)` | Group dispatch priority (0-31). Defaults to manifest priority if not set. |
| `.Enabled(bool)` | Kill switch for the entire group. Default: `true`. |

## Returns

- **Startup**: `SchedulerConfigurationBuilder` — for continued fluent chaining.
- **Runtime**: `Task<Manifest>` — the created or updated manifest record.

## Examples

### Startup Configuration (Recommended — Inferred Input Type)

```csharp
services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
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

Only the train interface type is specified. The input type (`SyncInput`) is inferred from `ISyncTrain : IServiceTrain<SyncInput, TOutput>` and validated at configuration time. The output type is not constrained — it can be `Unit` or any other type, and the output is discarded for background jobs.

### Runtime Scheduling

```csharp
public class MyService(ITraxScheduler scheduler)
{
    public async Task SetupSchedule()
    {
        var manifest = await scheduler.ScheduleAsync<ISyncTrain, SyncInput>(
            "sync-on-demand",
            new SyncInput { Source = "staging" },
            Every.Hours(1),
            options => options.Priority(15));
    }
}
```

## Remarks

- At startup, manifests are not created immediately — they are captured and seeded when the application starts via `SchedulerStartupService`.
- At runtime, `ScheduleAsync` creates/updates the manifest in the database immediately.
- The `externalId` is the primary key for upsert logic. Changing it creates a new manifest rather than updating the existing one.
- If the train type is not registered in the `TrainRegistry` (via `AddServiceTrainBus`), an `InvalidOperationException` is thrown.
- The group is determined by `.Group(...)` on `ScheduleOptions`. When not specified, it defaults to the `externalId`. Groups are auto-created (upserted by name) during scheduling. Orphaned groups are cleaned up on startup.
- For **one-off jobs** that should run once and auto-disable, use [ScheduleOnceAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/manifest-management.md %}#scheduleonceasync) instead of `ScheduleAsync`. It creates a manifest with `ScheduleType.Once` — no `Schedule` object needed. See [Delayed / One-Off Jobs]({{ site.baseurl }}{% link scheduler/delayed-jobs.md %}) for details.
