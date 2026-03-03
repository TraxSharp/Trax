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

*API Reference: [ScheduleMany]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %}) — all overloads, parameter tables, and examples including pruning.*

The builder captures the manifests and seeds them when the `BackgroundService` starts—same upsert semantics as `Schedule`.

### Grouping Manifests

Every manifest belongs to a **ManifestGroup**. A ManifestGroup is a first-class entity that ties related manifests together and exposes per-group dispatch controls (see [Per-Group Dispatch Controls](#per-group-dispatch-controls) below).

The group is set via the `ScheduleOptions` fluent builder using `.Group(...)`. When you don't specify a group, it defaults to the manifest's `externalId`—so every manifest always has a group, even if it's a group of one. ManifestGroups are upserted by name during scheduling: if a group with that name already exists it's reused, otherwise a new one is created automatically. Orphaned groups (groups with no remaining manifests) are cleaned up on startup.

```csharp
services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
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

*API Reference: [Schedule]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}), [ScheduleMany]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %})*

The dashboard's **Manifest Groups** page shows each group's settings and aggregate execution stats, which is useful when a logical operation is split across many manifests (e.g., syncing 1000 table slices).

### Runtime: ScheduleManyAsync

Use `ScheduleManyAsync` when the set of jobs is determined at runtime (loaded from a database, config file, or external API). It creates multiple manifests in a single transaction—if any fails, the entire batch rolls back. Both startup and runtime variants accept the same `ScheduleOptions` builder and use upsert semantics.

*API Reference: [ScheduleManyAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %})*

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
    await scheduler.ScheduleAsync<ISyncTableTrain, SyncTableInput>(
        $"sync-{config.Name}",
        new SyncTableInput { TableName = config.Name },
        Schedule.FromInterval(config.Interval),
        options => options.MaxRetries(config.Retries));
}
```

*API Reference: [ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}), [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions)*

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

*API Reference: [ScheduleManyAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %})*

### Pruning Stale Manifests

When the source collection shrinks between deployments—tables removed, slices reduced—old manifests stick around in the database. The name-based overload handles this automatically (`prunePrefix: "{name}-"`). With the explicit overload, specify `prunePrefix` manually. After upserting the batch, any existing manifests whose `ExternalId` starts with the prefix but weren't in the current batch are deleted—keeping the manifest table in sync with your source data.

*API Reference: [ScheduleMany — prunePrefix]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %})*

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

`IManifestScheduler` includes methods for runtime job control: `DisableAsync`, `EnableAsync`, `TriggerAsync`, `CancelAsync`, `CancelGroupAsync`, `ScheduleDependentAsync`, and `ScheduleOnceAsync`. Disabled jobs remain in the database but are skipped by the ManifestManager until re-enabled. `CancelAsync` and `CancelGroupAsync` cancel all in-progress executions of a manifest or group using dual-layer cancellation (database flag + same-server CTS).

`TriggerAsync` accepts an optional `TimeSpan delay` parameter to schedule a delayed execution of an existing manifest. `ScheduleOnceAsync` creates a new one-off manifest with `ScheduleType.Once` that fires after a delay and auto-disables on success. See [Delayed / One-Off Jobs](delayed-jobs.md) for usage patterns.

*API Reference: [DisableAsync / EnableAsync / TriggerAsync / CancelAsync / CancelGroupAsync / ScheduleOnceAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/manifest-management.md %}), [ScheduleDependentAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %})*

## Manifest Options

Configure per-job settings via the `ScheduleOptions` fluent builder:

```csharp
await scheduler.ScheduleAsync<IMyTrain, MyInput>(
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

*API Reference: [ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}), [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions)*

## Schedule Types

| Type | Use Case | API |
|------|----------|-----|
| `Interval` | Simple recurring | `Every.Minutes(5)` or `Schedule.FromInterval(TimeSpan)` |
| `Cron` | Traditional scheduling | `Cron.Daily()` or `Schedule.FromCron("0 3 * * *")` |
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

*API Reference: [ScheduleOptions — Exclude()]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions)*

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

For cron-based schedules, the scheduler estimates the cron frequency using a heuristic and applies the same boundary math. Precision will improve when the cron parser is upgraded to support full expression evaluation.

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

*API Reference: [AddScheduler]({{ site.baseurl }}{% link api-reference/scheduler-api/add-scheduler.md %}), [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions)*

## Timeout Enforcement

The ManifestManager actively cancels jobs that exceed their configured timeout. Each polling cycle, the `CancelTimedOutJobsStep` checks all InProgress metadata and cancels any where the elapsed time exceeds the manifest's `TimeoutSeconds` (or the global `DefaultJobTimeout`).

**Per-manifest timeout**: Set via `Timeout()` on `ScheduleOptions`:

```csharp
await scheduler.ScheduleAsync<IMyTrain, MyInput>(
    "my-job", new MyInput(), Every.Minutes(5),
    options => options.Timeout(TimeSpan.FromMinutes(10)));
```

**Global default**: Set via `DefaultJobTimeout()` on the scheduler builder (default: 20 minutes):

```csharp
.AddScheduler(scheduler => scheduler
    .DefaultJobTimeout(TimeSpan.FromMinutes(30)))
```

Timed-out jobs are cancelled using the same dual-layer mechanism as manual cancellation: `CancellationRequested = true` in the database (cross-server) plus `ICancellationRegistry.TryCancel()` for same-server instant cancel. The job transitions to `TrainState.Cancelled` — it is **not retried** and does **not create a dead letter**. This is distinct from dead-lettering, which handles jobs that have failed repeatedly.

*See also: [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %})*

## Configuration Options

Key options to know:

- **`ManifestManagerPollingInterval`** / **`JobDispatcherPollingInterval`** (default: 5 seconds each) — how often the ManifestManager and JobDispatcher poll independently. Use `PollingInterval` to set both to the same value
- **`MaxActiveJobs`** (default: 100) — global concurrent job cap; set to `null` for unlimited. Per-group limits can be set from code via `.Group(group => group.MaxActiveJobs(...))` or from the dashboard (see [Per-Group Dispatch Controls](#per-group-dispatch-controls))
- **`DefaultMaxRetries`** (default: 3) — retry attempts before dead-lettering
- **`DefaultJobTimeout`** (default: 20 minutes) — jobs exceeding this duration are actively cancelled (see [Timeout Enforcement](#timeout-enforcement))
- **`DefaultMisfirePolicy`** (default: `FireOnceNow`) — how missed runs are handled
- **`DefaultMisfireThreshold`** (default: 60 seconds) — grace period for misfire detection

*API Reference: [AddScheduler]({{ site.baseurl }}{% link api-reference/scheduler-api/add-scheduler.md %}) — full options table with all defaults including retry backoff, timeouts, and stuck job recovery.*
