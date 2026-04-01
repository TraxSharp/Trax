---
layout: default
title: Scheduling Options
parent: Scheduling
nav_order: 2
---

# Scheduling Options

## Bulk Scheduling

### Startup Configuration: ScheduleMany

For static bulk jobs, use the builder-time `ScheduleMany` during DI configuration. No async startup code or service resolution needed. The **name-based overload** derives `groupId`, `prunePrefix`, and the external ID prefix from a single `name` parameter. The **explicit overload** gives full control over each independently.

The builder captures the manifests and seeds them when the `BackgroundService` starts—same upsert semantics as `Schedule`.

### Grouping Manifests

Every manifest belongs to a **ManifestGroup**. A ManifestGroup is a first-class entity that ties related manifests together and exposes per-group dispatch controls (see [Per-Group Dispatch Controls](#per-group-dispatch-controls) below).

The group is set via the `ScheduleOptions` fluent builder using `.Group(...)`. When you don't specify a group, it defaults to the manifest's `externalId`—so every manifest always has a group, even if it's a group of one. ManifestGroups are upserted by name during scheduling: if a group with that name already exists it's reused, otherwise a new one is created automatically. Orphaned groups (groups with no remaining manifests) are cleaned up on startup.

```csharp
services.AddTrax(trax => trax
    .AddScheduler(scheduler => scheduler
        // Single manifest — explicit group shared with other related jobs
        .Schedule<IExtractTrain>(
            "extract-users",
            new ExtractInput { Table = "users" },
            Every.Minutes(5),
            options => options.Group("user-pipeline"))
        // Dependent manifest in the same group
        .Include<ILoadTrain>(
            "load-users",
            new LoadInput { Table = "users" },
            options => options.Group("user-pipeline"))
        // Bulk scheduling — name-based overload sets groupId automatically
        .ScheduleMany<ISyncTableTrain>(
            "table-sync",
            tables.Select(tableName => new ManifestItem(
                tableName,
                new SyncTableInput { TableName = tableName }
            )),
            Every.Minutes(5))
    )
);
```

The dashboard's **Manifest Groups** page shows each group's settings and aggregate execution stats, which is useful when a logical operation is split across many manifests (e.g., syncing 1000 table slices).

### Runtime: ScheduleManyAsync

Use `ScheduleManyAsync` when the set of jobs is determined at runtime (loaded from a database, config file, or external API). It creates multiple manifests in a single transaction—if any fails, the entire batch rolls back. Both startup and runtime variants accept the same `ScheduleOptions` builder and use upsert semantics.

### Configuration Per Item

The optional `configureEach` parameter receives each source item, so you can vary per-item settings. Use the `ScheduleOptions` builder for settings shared across the batch:

```csharp
var tableConfigs = new[]
{
    (Name: "users", Interval: TimeSpan.FromMinutes(1), Retries: 5),
    (Name: "orders", Interval: TimeSpan.FromMinutes(1), Retries: 5),
    (Name: "products", Interval: TimeSpan.FromMinutes(15), Retries: 3),
    (Name: "logs", Interval: TimeSpan.FromHours(1), Retries: 1),
};

foreach (var config in tableConfigs)
{
    await scheduler.ScheduleAsync<ISyncTableTrain, SyncTableInput, Unit>(
        $"sync-{config.Name}",
        new SyncTableInput { TableName = config.Name },
        Schedule.FromInterval(config.Interval),
        options => options.MaxRetries(config.Retries));
}
```

### Multi-Dimensional Bulk Jobs

For jobs split across multiple dimensions (e.g., table x slice index):

```csharp
var tables = new[]
{
    (Name: "customer", SliceCount: 100),
    (Name: "partner", SliceCount: 10),
    (Name: "user", SliceCount: 1000)
};

// Flatten with LINQ, schedule all in one transaction
var allJobs = tables.SelectMany(t =>
    Enumerable.Range(0, t.SliceCount).Select(slice => (t.Name, slice)));

await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, (string Table, int Slice)>(
    allJobs,
    item => (
        ExternalId: $"sync-{item.Table}-{item.Slice}",
        Input: new SyncTableInput { TableName = item.Table, SliceIndex = item.Slice }
    ),
    Every.Minutes(5));
```

### Pruning Stale Manifests

When the source collection shrinks between deployments—tables removed, slices reduced—old manifests stick around in the database. The name-based overload handles this automatically (`prunePrefix: "{name}-"`). With the explicit overload, specify `prunePrefix` manually. After upserting the batch, any existing manifests whose `ExternalId` starts with the prefix but weren't in the current batch are deleted—keeping the manifest table in sync with your source data.

Pruning runs in a **separate database context** after the main transaction commits. This means a prune failure (e.g., a transient database error) does not roll back successfully upserted manifests. The failure is logged as a warning and retried on the next startup or scheduling cycle.

## Per-Group Dispatch Controls

Each ManifestGroup has three configurable properties that govern how its manifests are dispatched:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxActiveJobs` | `int?` | `null` (unlimited) | Maximum concurrent active jobs (Pending + InProgress) within this group |
| `Priority` | `int` (0–31) | `0` | Dispatch ordering between groups—higher values are dispatched first |
| `IsEnabled` | `bool` | `true` | When `false`, all manifests in the group are skipped during queuing and dispatch |

These settings can be configured both from code via the `.Group(...)` builder on `ScheduleOptions`, and from the dashboard's **Manifest Group detail page**. Code-level configuration is applied during scheduling (upsert semantics), while the dashboard allows operators to adjust settings at runtime without redeployment.

```csharp
scheduler.Schedule<IMyTrain>(
    "my-job",
    new MyInput { ... },
    Every.Minutes(5),
    options => options
        .Priority(10)
        .Group("my-group", group => group
            .MaxActiveJobs(5)
            .Priority(20)
            .Enabled(true)));
```

**MaxActiveJobs** limits concurrent active jobs within a single group. When a group hits its cap, the [JobDispatcher](admin-trains/job-dispatcher.md) skips it and moves on to the next group—so other groups can still dispatch normally. This prevents a single high-throughput group from monopolizing all capacity. The global `MaxActiveJobs` (configured in code) still applies as an overall ceiling across all groups.

**Priority** determines the order in which groups are considered during dispatch. The JobDispatcher processes groups from highest priority (31) to lowest (0). If a high-priority group continually re-queues work, it is dispatched first—but because `MaxActiveJobs` caps how many jobs it can have active at once, lower-priority groups still get their fair share of capacity. This solves the starvation problem: priority controls *ordering*, while `MaxActiveJobs` controls *capacity*.

**IsEnabled** acts as a kill switch for an entire group. Disabling a group prevents its manifests from being queued or dispatched until re-enabled. This is useful during maintenance windows or when a downstream system is unavailable.

## Management Operations

`ITraxScheduler` includes methods for runtime job control: `DisableAsync`, `EnableAsync`, `TriggerAsync`, `CancelAsync`, `CancelGroupAsync`, `ScheduleDependentAsync`, and `ScheduleOnceAsync`. Disabled jobs remain in the database but are skipped by the ManifestManager until re-enabled. `CancelAsync` and `CancelGroupAsync` cancel all in-progress executions of a manifest or group using dual-layer cancellation (database flag + same-server CTS).

`TriggerAsync` accepts an optional `TimeSpan delay` parameter to schedule a delayed execution of an existing manifest. `ScheduleOnceAsync` creates a new one-off manifest with `ScheduleType.Once` that fires after a delay and auto-disables on success. See [Delayed / One-Off Jobs](delayed-jobs.md) for usage patterns.

## Manifest Options

Configure per-job settings via the `ScheduleOptions` fluent builder:

```csharp
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job",
    new MyInput { ... },
    Every.Hours(1),
    options => options
        .Enabled(true)                              // Default: true
        .MaxRetries(5)                              // Default: 3
        .Timeout(TimeSpan.FromMinutes(30))          // Null uses global default
        .Priority(10));                             // Default: 0
```

For dependent manifests that should only fire when explicitly activated by the parent train at runtime, add `.Dormant()`:

```csharp
scheduler
    .Schedule<IParentTrain>("parent", new ParentInput(), Every.Minutes(5))
    .Include<IChildTrain>(
        "child", new ChildInput(),
        options: o => o.Dormant());
```

See [Dormant Dependents](dependent-trains.md#dormant-dependents) for full details on registration and runtime activation.

## Schedule Types

| Type | Use Case | API |
|------|----------|-----|
| `Interval` | Simple recurring | `Every.Minutes(5)` or `Schedule.FromInterval(TimeSpan)` |
| `Cron` | Traditional scheduling (minute or second granularity) | `Cron.Daily()`, `Cron.Expression("*/15 * * * * *")`, or `Schedule.FromCron("0 3 * * *")` |
| `Dependent` | Runs after another manifest succeeds | `.ThenInclude()` / `.ThenIncludeMany()` / `.Include()` / `.IncludeMany()` or `ScheduleDependentAsync` |
| `DormantDependent` | Declared dependent, activated at runtime by parent | `.Include()` / `.IncludeMany()` with `.Dormant()` option, activated via `IDormantDependentContext` |
| `Once` | Fire-once delayed job, auto-disables on success | `ScheduleOnceAsync` or `.ScheduleOnce()` at startup |
| `None` | Manual trigger only | Use `scheduler.TriggerAsync(externalId)` |

See [Dependent Trains](dependent-trains.md) for details on chaining trains, and [Delayed / One-Off Jobs](delayed-jobs.md) for one-off scheduling.

## Exclusion Windows

Exclusion windows skip execution during specific periods — weekends, holidays, maintenance windows, or daily time ranges. Add exclusions via the `.Exclude()` method on `ScheduleOptions`:

```csharp
scheduler.Schedule<IReportTrain>(
    "daily-report",
    new ReportInput(),
    Cron.Daily(hour: 3),
    o => o
        .Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday))
        .Exclude(Exclude.Dates(new DateOnly(2026, 12, 25)))
        .Exclude(Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00"))));
```

Multiple exclusions can be combined — if ANY matches, the manifest is skipped. Excluded periods are "intentionally skipped", not misfires. When the excluded period ends, normal scheduling resumes and the misfire policy determines catch-up behavior.

Four built-in exclusion types: `DaysOfWeek`, `Dates`, `DateRange`, `TimeWindow` (supports midnight crossover).

See [Exclusion Windows](exclusions.md) for full details, examples, and misfire interaction.

## Schedule Variance

Schedule variance (jitter) adds a random delay to recurring schedules, preventing thundering-herd problems when many jobs share the same interval or cron expression. After each successful run, the next execution is delayed by a random value in the range `[0, variance]` seconds — jobs never fire earlier than their base schedule.

The computed next run time is stored in the database (`NextScheduledRun` column on the manifest), so it remains stable across polling cycles and is visible via SQL for debugging.

### Configuration

Variance can be set in two ways. On the schedule directly via `WithVariance()`:

```csharp
scheduler.Schedule<IScraperTrain>(
    "scraper",
    new ScraperInput(),
    Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2)));
```

Or via the `ScheduleOptions` fluent builder:

```csharp
scheduler.Schedule<IScraperTrain>(
    "scraper",
    new ScraperInput(),
    Cron.Daily(3),
    o => o.Variance(TimeSpan.FromMinutes(30)));
```

If both are specified, `Schedule.WithVariance()` takes precedence over `ScheduleOptions.Variance()`.

### How It Works

1. A manifest completes successfully — `LastSuccessfulRun` is set to now
2. `ComputeNextScheduledRun` calculates: base next time + random `[0, variance]` seconds
   - **Interval**: base = `LastSuccessfulRun + IntervalSeconds`
   - **Cron**: base = next cron occurrence after `LastSuccessfulRun`
3. The result is persisted to `NextScheduledRun` in the database
4. On each polling cycle, `ShouldRunNow` checks `NextScheduledRun` — if it's in the past, the job fires; if in the future, it waits

When `NextScheduledRun` is null (first run, or variance not configured), the scheduler falls back to standard interval/cron evaluation.

### Constraints

- Variance is only supported on `Interval` and `Cron` schedule types. Applying it to `Dependent`, `Once`, or other types throws `InvalidOperationException` at configuration time.
- Variance must be non-negative.
- Variance can exceed the interval (e.g., 5-minute variance on a 1-minute interval) — this is allowed but means runs may be delayed well past the next base interval.

### Interaction with Other Features

- **Exclusion windows**: Exclusions are checked after `NextScheduledRun` is due — if the current time falls in an exclusion window, the job is skipped even if `NextScheduledRun` is in the past.
- **Misfire policies**: Work the same as without variance. If a job with `DoNothing` policy misses its `NextScheduledRun` by more than the misfire threshold, it's skipped. `FireOnceNow` fires immediately regardless.

## Misfire Policies

A **misfire** occurs when a scheduled job was supposed to fire but couldn't — the scheduler was down, or the job was blocked by an active execution or dead letter. When the scheduler recovers, the misfire policy determines what happens.

### Policies

| Policy | Behavior |
|--------|----------|
| `FireOnceNow` | Fire once immediately if overdue. This is the default and preserves backward compatibility. |
| `DoNothing` | If overdue beyond the misfire threshold, skip and wait for the next natural occurrence. |

### Misfire Threshold

The misfire threshold is a grace period. If a job is overdue by less than the threshold, it fires normally regardless of policy. Only when the overdue period exceeds the threshold does the policy take effect.

- **Global default**: `DefaultMisfireThreshold` (default: 60 seconds) — set via `AddScheduler`
- **Per-manifest override**: `MisfireThreshold(TimeSpan)` on `ScheduleOptions` — overrides the global default for a single manifest

### How DoNothing Works

For interval-based schedules, the scheduler finds the most recent interval boundary relative to `LastSuccessfulRun` and checks if the current time is within the misfire threshold of that boundary. If yes, fire. If no, wait for the next boundary.

**Example**: A job runs every 5 minutes with a 60-second threshold. The scheduler goes down at 10:00 and comes back at 13:02:

- Most recent 5-minute boundary from `LastSuccessfulRun` (10:00): **13:00**
- Time since boundary: 2 minutes (120 seconds) > 60-second threshold → **skip**
- Next boundary: **13:05** — the job fires then (if within threshold)

If the scheduler comes back at 13:00:30 instead:

- Most recent boundary: **13:00**
- Time since boundary: 30 seconds ≤ 60-second threshold → **fire**

For cron-based schedules, the scheduler uses precise next-occurrence calculation (via the Cronos library) to find the most recent cron boundary before now and checks if the current time is within the misfire threshold of that boundary. Both 5-field and 6-field (seconds) cron expressions are supported.

> **Seconds-granularity cron:** When using 6-field cron expressions with second-level precision, ensure the `ManifestManagerPollingInterval` is set appropriately. The default 5-second polling interval means the scheduler checks for due manifests every 5 seconds. For "every 10 seconds" cron (`*/10 * * * * *`), the default polling is adequate. For "every second" cron (`* * * * * *`), reduce the polling interval accordingly.

### Configuration

```csharp
// Global defaults
.AddScheduler(scheduler => scheduler
    .DefaultMisfirePolicy(MisfirePolicy.FireOnceNow)
    .DefaultMisfireThreshold(TimeSpan.FromSeconds(60))
    // ...
)

// Per-manifest override
.Schedule<ISyncTrain>(
    "sync-daily",
    new SyncInput(),
    Cron.Daily(3, 0),
    options => options
        .OnMisfire(MisfirePolicy.DoNothing)
        .MisfireThreshold(TimeSpan.FromMinutes(10)))
```

Misfire policies only apply to `Cron` and `Interval` schedule types. Dependent manifests fire based on parent completion, not time — misfire policies are ignored for them.

## Timeout Enforcement

The ManifestManager actively cancels jobs that exceed their configured timeout. Each polling cycle, the `CancelTimedOutJobsJunction` checks all InProgress metadata and cancels any where the elapsed time exceeds the manifest's `TimeoutSeconds` (or the global `DefaultJobTimeout`).

**Per-manifest timeout**: Set via `Timeout()` on `ScheduleOptions`:

```csharp
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job", new MyInput(), Every.Minutes(5),
    options => options.Timeout(TimeSpan.FromMinutes(10)));
```

**Global default**: Set via `DefaultJobTimeout()` on the scheduler builder (default: 20 minutes):

```csharp
.AddScheduler(scheduler => scheduler
    .DefaultJobTimeout(TimeSpan.FromMinutes(30)))
```

Timed-out jobs are cancelled using the same dual-layer mechanism as manual cancellation: `CancellationRequested = true` in the database (cross-server) plus `ICancellationRegistry.TryCancel()` for same-server instant cancel. The job transitions to `TrainState.Cancelled` — it is **not retried** and does **not create a dead letter**. This is distinct from dead-lettering, which handles jobs that have failed repeatedly.

*See also: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens)*

## Configuration Options

Key options to know:

- **`ManifestManagerPollingInterval`** / **`JobDispatcherPollingInterval`** (default: 5 seconds each) — how often the ManifestManager and JobDispatcher poll independently. Use `PollingInterval` to set both to the same value
- **`MaxActiveJobs`** (default: 10) — global concurrent job cap; set to `null` for unlimited. Per-group limits can be set from code via `.Group(group => group.MaxActiveJobs(...))` or from the dashboard (see [Per-Group Dispatch Controls](#per-group-dispatch-controls))
- **`DefaultMaxRetries`** (default: 3) — retry attempts before dead-lettering
- **`DefaultRetryDelay`** (default: 5 minutes) — base delay between retry attempts. Combined with `RetryBackoffMultiplier` for exponential backoff
- **`RetryBackoffMultiplier`** (default: 2.0) — multiplier applied to each subsequent retry delay (e.g., 5m, 10m, 20m). Set to `1.0` for constant delay
- **`MaxRetryDelay`** (default: 1 hour) — maximum retry delay cap, prevents unbounded backoff growth
- **`DeadLetterRetentionPeriod`** (default: 30 days) — how long resolved dead letters are kept before auto-purge
- **`AutoPurgeDeadLetters`** (default: true) — enable automatic deletion of resolved dead letters past the retention period
- **`DefaultJobTimeout`** (default: 20 minutes) — jobs exceeding this duration are actively cancelled (see [Timeout Enforcement](#timeout-enforcement))
- **`DefaultMisfirePolicy`** (default: `FireOnceNow`) — how missed runs are handled
- **`DefaultMisfireThreshold`** (default: 60 seconds) — grace period for misfire detection

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [TriggerAsync / DisableAsync / EnableAsync / CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management)
