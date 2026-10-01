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

The builder captures the manifests and seeds them when the `BackgroundService` starts, same upsert semantics as `Schedule`.

### Grouping Manifests

Every manifest belongs to a **ManifestGroup**. A ManifestGroup is a first-class entity that ties related manifests together and exposes per-group dispatch controls (see [Per-Group Dispatch Controls](#per-group-dispatch-controls) below).

The group is set via the `ScheduleOptions` fluent builder using `.Group(...)`. When you don't specify a group, it defaults to the manifest's `externalId`, so every manifest always has a group, even if it's a group of one. ManifestGroups are upserted by name during scheduling: if a group with that name already exists it's reused, otherwise a new one is created automatically. Orphaned groups (groups with no remaining manifests) are cleaned up on startup.

```csharp
services.AddTrax(trax => trax
    .AddScheduler(scheduler => scheduler
        // Single manifest, explicit group shared with other related jobs
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
        // Bulk scheduling, name-based overload sets groupId automatically
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

Use `ScheduleManyAsync` when the set of jobs is determined at runtime (loaded from a database, config file, or external API). It creates multiple manifests in a single transaction, if any fails, the entire batch rolls back. Both startup and runtime variants accept the same `ScheduleOptions` builder and use upsert semantics.

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

await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, (string Table, int Slice)>(
    allJobs,
    item => (
        ExternalId: $"sync-{item.Table}-{item.Slice}",
        Input: new SyncTableInput { TableName = item.Table, SliceIndex = item.Slice }
    ),
    Every.Minutes(5));
```

### Pruning Stale Manifests

When the source collection shrinks between deployments, tables removed, slices reduced, old manifests stick around in the database. The name-based overload handles this automatically (its prune prefix is `"{name}-"`). With the explicit overload, set one with `options => options.PrunePrefix("...")`. After upserting the batch, any existing manifests whose `ExternalId` starts with the prefix but weren't in the current batch are deleted, keeping the manifest table in sync with your source data.

A name-based batch prunes only manifests in its own group, so `ScheduleMany("sync", ...)` never deletes the manifests of `ScheduleMany("sync-users", ...)`. An explicit `prunePrefix` has no such scope, so `AddScheduler` fails at startup when one batch's prefix starts another batch's (for example `"sync-"` and `"sync-users-"`), unless the shorter one is a name-based batch in a different group. It also fails when a single `Schedule` falls inside a batch's prune (its external ID starts with the prefix and, for a name-based batch, it is in the batch's group), since the prune would delete it at every start. A manifest with a pending or running run is never pruned; it goes at a later prune, once the run has finished.

Pruning runs in a **separate database context** after the main transaction commits. This means a prune failure (e.g., a transient database error) does not roll back successfully upserted manifests. The failure is logged as a warning and retried on the next startup or scheduling cycle.

## Per-Group Dispatch Controls

Each ManifestGroup has three configurable properties that govern how its manifests are dispatched:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxActiveJobs` | `int?` | `null` (unlimited) | Maximum concurrent active jobs (Pending + InProgress) within this group |
| `Priority` | `int` (0–31) | `0` | Dispatch ordering between groups. higher values are dispatched first |
| `IsEnabled` | `bool` | `true` | When `false`, all manifests in the group are skipped during queuing and dispatch |

These settings can be configured both from code via the `.Group(...)` builder on `ScheduleOptions`, and from the dashboard's **Manifest Group detail page**. The dashboard allows operators to adjust settings at runtime without redeployment, and code writes only the settings it states (see [What a Restart Rewrites](#what-a-restart-rewrites)).

Several manifests can share one group. A setting only one member states applies to the whole group; members that say nothing leave it alone. Two members that state different values for the same setting fail `AddScheduler` at startup, with an error naming both, because each start would otherwise overwrite one with the other.

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

**MaxActiveJobs** limits concurrent active jobs within a single group. When a group hits its cap, the [JobDispatcher](admin-trains/job-dispatcher.md) skips it and moves on to the next group, so other groups can still dispatch normally. This prevents a single high-throughput group from monopolizing all capacity. The global `MaxActiveJobs` (configured in code) still applies as an overall ceiling across all groups. Both limits are counted by each dispatching host on its own, so with N hosts dispatching the total can reach N times either one (see [Capacity Limit Approximation](/docs/scheduler/concurrency#capacity-limit-approximation)).

**Priority** determines the order in which groups are considered during dispatch. The JobDispatcher processes groups from highest priority (31) to lowest (0). If a high-priority group continually re-queues work, it is dispatched first, but because `MaxActiveJobs` caps how many jobs it can have active at once, lower-priority groups still get their fair share of capacity. This solves the starvation problem: priority controls *ordering*, while `MaxActiveJobs` controls *capacity*.

The manifest's own `.Priority(...)` orders work within a group: each scheduled run is queued at its manifest's priority, as a manual trigger or a dead-letter requeue is. When a manifest has a group of its own (no group name), or a name-based batch uses its own group, the manifest's stated priority is also the group's. In a shared group, a new group starts at the priority of the first manifest seeded into it, and after that changes only when a member states `.Group(..., g => g.Priority(...))`.

**IsEnabled** acts as a kill switch for an entire group. Disabling a group prevents its manifests from being queued or dispatched until re-enabled. This is useful during maintenance windows or when a downstream system is unavailable.

### What a Restart Rewrites

Every host start schedules its manifests again (an upsert by `ExternalId`). The schedule, input and train always come from code. The settings an operator can change at runtime are written only when the code states them:

| Setting | Stated in code | Not stated |
|---|---|---|
| Manifest `Enabled(...)` | Written at every start | New manifest: enabled. Existing: keeps its value, including a runtime disable |
| Manifest `MaxRetries(...)` | Written at every start | New manifest: `DefaultMaxRetries`. Existing: keeps its value, including a runtime change; a later change to `DefaultMaxRetries` does not reach it |
| Manifest `Timeout(...)` | Written at every start | New manifest: no timeout of its own (`DefaultJobTimeout` applies). Existing: keeps its value |
| Manifest `Priority(...)` | Written at every start | New manifest: 0. Existing: keeps its value |
| Manifest `FailureWindow(...)` | Written at every start | New manifest: none (`FailureCountWindow` applies). Existing: keeps its value |
| Group `MaxActiveJobs(...)` | Written at every start | New group: no limit. Existing: keeps its value |
| Group `Priority(...)` | Written at every start | New group: the manifest's priority. Existing: keeps its value |
| Group `Enabled(...)` | Written at every start | New group: enabled. Existing: keeps its value |

So a manifest or group disabled from the dashboard as a kill switch stays disabled across deploys, crashes and scale-outs, unless the code says `.Enabled(true)`. To hand a setting back to code, state it; removing a stated value from code leaves the last value in place.

## Management Operations

`ITraxScheduler` includes methods for runtime job control: `DisableAsync`, `EnableAsync`, `TriggerAsync`, `CancelAsync`, `CancelGroupAsync`, `ScheduleDependentAsync`, and `ScheduleOnceAsync`. Disabled jobs remain in the database but are skipped by the ManifestManager until re-enabled. Work a disabled job had already queued waits as well, a retry waiting out its backoff included, and is dispatched once the job is re-enabled; a run someone asked for by name (`TriggerAsync`, `TriggerGroupAsync`, a dead-letter requeue) runs either way. `CancelAsync` and `CancelGroupAsync` cancel all in-progress executions of a manifest or group using dual-layer cancellation (database flag + same-server CTS).

`TriggerAsync` accepts an optional `TimeSpan delay` parameter to schedule a delayed execution of an existing manifest. `ScheduleOnceAsync` creates a new one-off manifest with `ScheduleType.Once` that fires after a delay and auto-disables on success. See [Delayed / One-Off Jobs](delayed-jobs.md) for usage patterns.

### Disabling a job

Disabling a manifest (`DisableAsync`, the dashboard, or `IsEnabled = false`) stops it until it is re-enabled:

- The ManifestManager does not queue it on its schedule, and a dependent is not queued after its parent succeeds.
- A dormant dependent is not activated by its parent. `IDormantDependentContext.ActivateAsync` logs a warning and skips it, as it does for a disabled manifest group.
- Work the manifest already has queued (a retry waiting out its backoff, say) is held by the dispatcher. It stays `Queued` and is dispatched once the manifest is re-enabled.

A run someone asked for by name is the exception, because the operator asked for exactly that run: an entry queued by `TriggerAsync`, `TriggerGroupAsync` or a dead-letter requeue runs whether or not the manifest is enabled, after its delay if it has one. `TriggerGroupAsync` triggers only the group's enabled members, but the entries it queues run even if a member is disabled afterwards. A trigger that finds the manifest's entry already waiting marks that entry instead of queueing another, which releases it the same way. The manifest stays disabled, so its schedule does not resume. A disabled manifest group still holds everything, explicit or not.

## Manifest Options

Configure per-job settings via the `ScheduleOptions` fluent builder:

```csharp
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job",
    new MyInput { ... },
    Every.Hours(1),
    options => options
        .Enabled(true)                              // Unstated: new manifests enabled, existing ones unchanged
        .MaxRetries(5)                              // Unstated: DefaultMaxRetries (3)
        .Timeout(TimeSpan.FromMinutes(30))          // Null uses global default
        .Priority(10));                             // Default: 0
```

`MaxRetries(n)` is the number of retries after the first run, so a manifest runs at most n + 1 times in a row before it is dead-lettered. `MaxRetries(0)` runs the job once and dead-letters it on its first failure; the default of 3 allows four attempts. A negative value is refused. Only failures inside the manifest's failure window count, and only those after the manifest's latest resolved dead letter. The window is the scheduler's [`FailureCountWindow`](#configuration-options) unless the manifest states its own with `.FailureWindow(TimeSpan)`: a short one for a job that runs every minute, whose old failures say nothing about its health, or a long one for a daily job that should still dead-letter after failing several days in a row. Stored in whole seconds; throws `ArgumentOutOfRangeException` unless between one second and ten years. Written to an existing manifest only when stated, so a manifest that stops stating it keeps the window it has.

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

Exclusion windows skip execution during specific periods, weekends, holidays, maintenance windows, or daily time ranges. Add exclusions via the `.Exclude()` method on `ScheduleOptions`:

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

Multiple exclusions can be combined, if ANY matches, the manifest is skipped. Excluded periods are "intentionally skipped", not misfires. When the excluded period ends, normal scheduling resumes and the misfire policy determines catch-up behavior.

Four built-in exclusion types: `DaysOfWeek`, `Dates`, `DateRange`, `TimeWindow` (supports midnight crossover).

See [Exclusion Windows](exclusions.md) for full details, examples, and misfire interaction.

## Schedule Variance

Schedule variance (jitter) adds a random delay to recurring schedules, preventing thundering-herd problems when many jobs share the same interval or cron expression. After each successful run, the next execution is delayed by a random value in the range `[0, variance]` seconds, jobs never fire earlier than their base schedule.

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

1. A manifest completes successfully. `LastSuccessfulRun` is set to now
2. `ComputeNextScheduledRun` calculates: base next time + random `[0, variance]` seconds
   - **Interval**: base = `LastSuccessfulRun + IntervalSeconds`
   - **Cron**: base = next cron occurrence after `LastSuccessfulRun`
3. The result is persisted to `NextScheduledRun` in the database
4. On each polling cycle, `ShouldRunNow` checks `NextScheduledRun`, if it's in the past, the job fires; if in the future, it waits

`NextScheduledRun` also holds a new cron's first run: scheduling a cron that has never succeeded stores its first occurrence after the scheduling time, with no jitter, so variance applies from the second run. When `NextScheduledRun` is null (variance not configured, or an interval that has never run), the scheduler falls back to standard interval/cron evaluation, and an interval that has never run fires on the next poll.

A cancelled run later than the last success resets the variance for one run. The schedule is then evaluated from the cancelled run and the stored `NextScheduledRun` is ignored, because it was computed from the earlier success, so the next run is the next base time after the cancelled run with no jitter. The next success computes a jittered `NextScheduledRun` again.

### Constraints

- Variance applies only to `Interval` and `Cron` schedules. `ScheduleOptions.Variance()` on a dependent (`ThenInclude`, `Include`) or one-off (`ScheduleOnce`) manifest is ignored: nothing is stored and nothing throws.
- Variance must be non-negative. A negative value throws `InvalidOperationException` when the manifest is scheduled, which for the builder is at startup seeding.
- Variance can exceed the interval (e.g., 5-minute variance on a 1-minute interval), this is allowed but means runs may be delayed well past the next base interval.

### Interaction with Other Features

- **Exclusion windows**: Exclusions are checked after `NextScheduledRun` is due, if the current time falls in an exclusion window, the job is skipped even if `NextScheduledRun` is in the past.
- **Misfire policies**: Work the same as without variance. If a job with `DoNothing` policy misses its `NextScheduledRun` by more than the misfire threshold, it's skipped. `FireOnceNow` fires immediately regardless.

## Misfire Policies

A **misfire** occurs when a scheduled job was supposed to fire but couldn't, the scheduler was down, or the job was blocked by an active execution or dead letter. When the scheduler recovers, the misfire policy determines what happens.

### Policies

| Policy | Behavior |
|--------|----------|
| `FireOnceNow` | Fire once immediately if overdue. This is the default and preserves backward compatibility. |
| `DoNothing` | If overdue beyond the misfire threshold, skip and wait for the next natural occurrence. |

### Misfire Threshold

The misfire threshold is a grace period. If a job is overdue by less than the threshold, it fires normally regardless of policy. Only when the overdue period exceeds the threshold does the policy take effect.

- **Global default**: `DefaultMisfireThreshold` (default: 60 seconds), set via `AddScheduler`
- **Per-manifest override**: `MisfireThreshold(TimeSpan)` on `ScheduleOptions`, overrides the global default for a single manifest

### How DoNothing Works

For interval-based schedules, the scheduler finds the most recent interval boundary relative to `LastSuccessfulRun` and checks if the current time is within the misfire threshold of that boundary. If yes, fire. If no, wait for the next boundary.

**Example**: A job runs every 5 minutes with a 60-second threshold. The scheduler goes down at 10:00 and comes back at 13:02:

- Most recent 5-minute boundary from `LastSuccessfulRun` (10:00): **13:00**
- Time since boundary: 2 minutes (120 seconds) > 60-second threshold → **skip**
- Next boundary: **13:05**: the job fires then (if within threshold)

If the scheduler comes back at 13:00:30 instead:

- Most recent boundary: **13:00**
- Time since boundary: 30 seconds ≤ 60-second threshold → **fire**

For cron-based schedules, the scheduler uses the Cronos library to find the latest cron occurrence at or before now, in UTC, and checks if the current time is within the misfire threshold of that occurrence. The search takes the same few dozen evaluations however long the scheduler was down, so a secondly cron that missed two days, or a minutely cron that missed a year, fires at its next occurrence like any other. Both 5-field and 6-field (seconds) cron expressions are supported.

A cron that has never succeeded counts from its first occurrence after it was scheduled, not from when it was scheduled. A cron manifest written by an earlier Trax version that has never succeeded and has no stored `NextScheduledRun` is instead due on the next poll; scheduling it again, which the builder does for its own manifests at every start, records its first occurrence.

> **Seconds-granularity cron:** When using 6-field cron expressions with second-level precision, verify the `ManifestManagerPollingInterval` is set appropriately. The default 5-second polling interval means the scheduler checks for due manifests every 5 seconds. For "every 10 seconds" cron (`*/10 * * * * *`), the default polling is adequate. For "every second" cron (`* * * * * *`), reduce the polling interval accordingly.

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

Misfire policies only apply to `Cron` and `Interval` schedule types. Dependent manifests fire based on parent completion, not time, misfire policies are ignored for them.

## Timeout Enforcement

The ManifestManager actively cancels jobs that exceed their configured timeout. Each polling cycle, the `CancelTimedOutJobsJunction` checks every InProgress run and cancels any that has run longer than its timeout. A run's timeout is resolved from the run at the root of its `ParentId` chain:

- The root's manifest `TimeoutSeconds`, when it sets one. A train nested inside a scheduled run (a sub-train with a `ParentId` and no manifest of its own) shares that run's timeout, so a 30-minute sub-train of a manifest with a two-hour `Timeout` is not cut off at 20 minutes.
- Otherwise `DefaultJobTimeout`, but only when a scheduler dispatched the root: it has a manifest, a work queue entry names it (a queued train), or a `background_job` row runs it (a local-worker job).
- Otherwise none. A train run directly on the train bus by any host that shares the database is not timed out; the stale in-progress reaper remains its safety net. So is a chain nested deeper than 16 levels, or one whose parent row is missing, since the canceller never cancels on a guess.

Runs of a manifest that was disabled while they ran are still timed out: disabling a manifest stops new runs, it does not exempt a running one. The scheduler's own trains, and trains excluded with `ExcludeFromMaxActiveJobs`, are not timed out, and neither is a train nested inside one.

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

Timed-out jobs are cancelled using the same dual-layer mechanism as manual cancellation: `CancellationRequested = true` in the database (cross-server) plus `ICancellationRegistry.TryCancel()` for same-server instant cancel. The job transitions to `TrainState.Cancelled`, it is **not retried** and does **not create a dead letter**. This is distinct from dead-lettering, which handles jobs that have failed repeatedly.

A cancelled run, whether it timed out or an operator cancelled it, **consumes the occurrence it ran for**. The ManifestManager evaluates a schedule from whichever is later, the last successful run or the last cancelled one, so an hourly job whose run is cancelled at 10:20 next runs at 11:20, not on the next polling cycle. A job that always exceeds its timeout therefore runs once per occurrence rather than back to back. A `Once` manifest whose run was cancelled is not run again (trigger it to run it), and a dependent whose run was cancelled waits for its parent's next success.

The stale in-progress reaper, the safety net for workers that died mid-run, resolves each run's timeout the same way and fails it only at the later of `StaleInProgressTimeout` and that timeout plus the grace between `DefaultJobTimeout` and `StaleInProgressTimeout` (40 minutes with the defaults), never while it is still inside its timeout. That holds for a nested run, which is kept as long as the scheduled run it belongs to, and for a `DefaultJobTimeout` set longer than `StaleInProgressTimeout`. See [ManifestManager](/docs/scheduler/admin-trains/manifest-manager#reapstaleinprogressmetadatajunction).

A scheduler job timeout is a cancellation the scheduler asked for. A timeout inside your train is not: when an `HttpClient` gives up on a slow upstream it throws `TaskCanceledException`, and the run is recorded `Failed`, classified `Transient` (unless your `IFailureClassifier` says otherwise). That failure counts toward the manifest's `MaxRetries`, so the manifest runs it again and dead-letters it once the retries are used up.

*See also: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens)*

## Configuration Options

Key options to know:

- **`ManifestManagerPollingInterval`** (default: 5 seconds) / **`JobDispatcherPollingInterval`** (default: 2 seconds), how often the ManifestManager and JobDispatcher poll independently. Use `PollingInterval` to set both to the same value
- **`MaxActiveJobs`** (default: 10), global concurrent job cap; set to `null` for unlimited. Each dispatching host counts on its own, so with N hosts the total can reach N times the cap (see [Capacity Limit Approximation](/docs/scheduler/concurrency#capacity-limit-approximation)). Per-group limits can be set from code via `.Group(group => group.MaxActiveJobs(...))` or from the dashboard (see [Per-Group Dispatch Controls](#per-group-dispatch-controls))
- **`DefaultMaxRetries`** (default: 3), retries after the first run before dead-lettering (the default allows four attempts), for a new manifest whose options do not set `MaxRetries`. An existing manifest keeps its stored value at a re-seed, so a change, in code or at runtime, applies only to manifests created after it
- **`FailureCountWindow`** (default: 24 hours), how far back a manifest's failed runs count toward its retry backoff and its `MaxRetries`. A failure older than the window no longer delays the next run or counts toward a dead letter, so occasional failures weeks apart do not dead-letter a healthy manifest. A success does not reset the count inside the window, but it does end the backoff: only a run after a failed one is delayed. Set with `FailureCountWindow(TimeSpan)`; must be between one second and ten years. A manifest that states `.FailureWindow(TimeSpan)` uses its own window instead. The scheduler logs a warning at startup when the backoff for `DefaultMaxRetries` retries alone reaches the window, since a manifest that always fails would then never be dead-lettered
- **`DefaultRetryDelay`** (default: 5 minutes), base delay between retry attempts. Combined with `RetryBackoffMultiplier` for exponential backoff. The delay applies only when the manifest's latest finished run failed; the run after a success or a cancel goes on time
- **`RetryBackoffMultiplier`** (default: 2.0), multiplier applied to each subsequent retry delay (e.g., 5m, 10m, 20m). Set to `1.0` for constant delay
- **`MaxRetryDelay`** (default: 1 hour), maximum retry delay cap, prevents unbounded backoff growth
- **`DeadLetterRetentionPeriod`** (default: 30 days), how long resolved dead letters are kept before auto-purge
- **`AutoPurgeDeadLetters`** (default: true), enable automatic deletion of resolved dead letters past the retention period
- **`DefaultJobTimeout`** (default: 20 minutes), runs a scheduler dispatched whose manifest sets no timeout, and trains nested in them, are actively cancelled after this long; a train run directly on the train bus is not (see [Timeout Enforcement](#timeout-enforcement))
- **`DefaultMisfirePolicy`** (default: `FireOnceNow`), how missed runs are handled, for a manifest whose options do not call `OnMisfire`. A runtime change applies to manifests seeded after it, which includes every manifest that does not call `OnMisfire` at the next start
- **`DefaultMisfireThreshold`** (default: 60 seconds), grace period for misfire detection

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [TriggerAsync / DisableAsync / EnableAsync / CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management)
