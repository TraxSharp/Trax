---
layout: default
title: Scheduling Helpers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 7
---

# Scheduling Helpers

Helper classes for defining when and how scheduled jobs run: the `Every` and `Cron` factory classes for creating `Schedule` objects, the `Schedule` record itself, and `ManifestOptions` for per-job configuration.

---

## Every

Static factory class for creating **interval-based** schedules.

```csharp
public static class Every
```

The shortest interval is one second. A zero or negative count throws `ArgumentOutOfRangeException` at the call, since the manifest stores whole seconds and would otherwise store zero. A new interval schedule runs on the first poll after it is scheduled, then once per interval after each success.

| Method | Signature | Description |
|--------|-----------|-------------|
| `Seconds` | `static Schedule Seconds(int seconds)` | Run every N seconds |
| `Minutes` | `static Schedule Minutes(int minutes)` | Run every N minutes |
| `Hours` | `static Schedule Hours(int hours)` | Run every N hours |
| `Days` | `static Schedule Days(int days)` | Run every N days |

### Examples

```csharp
Every.Seconds(30)   // Every 30 seconds
Every.Minutes(5)    // Every 5 minutes
Every.Hours(2)      // Every 2 hours
Every.Days(1)       // Every day
```

---

## Cron

Static factory class for creating **cron-based** schedules with readable methods. Supports both standard 5-field (minute granularity) and 6-field (second granularity) cron formats. For complex expressions, use `Cron.Expression()`.

**Every cron time is UTC.** `Cron.Daily(hour: 3)` runs at 03:00 UTC, whatever the host's time zone.

Each method builds its schedule through `Schedule.FromCron`, which parses the expression, so a value out of range throws `FormatException` at the call that states it: `Cron.Daily(hour: 25)`, `Cron.Hourly(minute: 60)`, or a malformed `Cron.Expression(...)`. So does a valid expression with no occurrence in the next ten years, such as `0 0 30 2 *` (February 30th), with a message saying it never fires; a cron that fires only in leap years is accepted. A cron stored some other way that has no occurrence at all is never due.

A new cron schedule first runs at **its first occurrence after it is scheduled**, not on the next poll: `Cron.Daily(hour: 3)` scheduled at 14:00 first runs at 03:00 the next day. Scheduling records that occurrence on the manifest's `NextScheduledRun`, and re-stating the same cron at a restart keeps it. A cron manifest written by an earlier Trax version that has never succeeded and has no stored `NextScheduledRun` is due on the next poll instead; scheduling it again, which the builder does for its own manifests at every start, records its first occurrence.

```csharp
public static class Cron
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Secondly` | `static Schedule Secondly()` | Every second (`* * * * * *`) |
| `Minutely` | `static Schedule Minutely()` | Every minute (`* * * * *`) |
| `Minutely` | `static Schedule Minutely(int second)` | Every minute at the specified second |
| `Hourly` | `static Schedule Hourly(int minute = 0, int second = 0)` | Every hour at the specified minute/second |
| `Daily` | `static Schedule Daily(int hour = 0, int minute = 0, int second = 0)` | Every day at the specified time |
| `Weekly` | `static Schedule Weekly(DayOfWeek day, int hour = 0, int minute = 0, int second = 0)` | Every week on the specified day/time |
| `Monthly` | `static Schedule Monthly(int day = 1, int hour = 0, int minute = 0, int second = 0)` | Every month on the specified day/time |
| `Expression` | `static Schedule Expression(string cronExpression)` | From a raw 5-field or 6-field cron string |

When a `second` parameter is 0 (the default), the method produces a standard 5-field expression. When `second` is non-zero, it produces a 6-field expression with seconds.

### Examples

```csharp
// 5-field (minute granularity)
Cron.Minutely()                              // Every minute
Cron.Hourly(minute: 30)                      // Every hour at :30
Cron.Daily(hour: 3)                          // Daily at 03:00 UTC
Cron.Daily(hour: 14, minute: 30)             // Daily at 14:30 UTC
Cron.Weekly(DayOfWeek.Monday, hour: 9)       // Every Monday at 09:00 UTC
Cron.Monthly(day: 15, hour: 0)               // 15th of each month at midnight UTC
Cron.Expression("0 */6 * * *")              // Every 6 hours (custom cron)

// 6-field (second granularity)
Cron.Secondly()                              // Every second
Cron.Minutely(second: 30)                   // Every minute at :30 seconds
Cron.Hourly(minute: 15, second: 45)         // Every hour at 15:45
Cron.Daily(hour: 3, minute: 0, second: 30)  // Daily at 03:00:30 UTC
Cron.Expression("*/15 * * * * *")           // Every 15 seconds (custom 6-field)
```

### Cron Expression Format

Trax supports both standard 5-field and 6-field (with seconds) cron formats. The format is auto-detected by counting fields. Expressions are evaluated in UTC.

#### 5-field (minute granularity): `minute hour day-of-month month day-of-week`

| Field | Range | Special Characters |
|-------|-------|--------------------|
| Minute | 0-59 | `*` `,` `-` `/` |
| Hour | 0-23 | `*` `,` `-` `/` |
| Day of month | 1-31 | `*` `,` `-` `/` |
| Month | 1-12 | `*` `,` `-` `/` |
| Day of week | 0-6 (0 = Sunday) | `*` `,` `-` `/` |

#### 6-field (second granularity): `second minute hour day-of-month month day-of-week`

| Field | Range | Special Characters |
|-------|-------|--------------------|
| Second | 0-59 | `*` `,` `-` `/` |
| Minute | 0-59 | `*` `,` `-` `/` |
| Hour | 0-23 | `*` `,` `-` `/` |
| Day of month | 1-31 | `*` `,` `-` `/` |
| Month | 1-12 | `*` `,` `-` `/` |
| Day of week | 0-6 (0 = Sunday) | `*` `,` `-` `/` |

> **Note:** 7-field cron (with year) is not supported. The effective resolution of seconds-granularity cron is limited by the `ManifestManagerPollingInterval` (default: 5 seconds).

---

## Schedule (Record)

An immutable record that represents a schedule definition. Created by `Every`, `Cron`, or the static factory methods.

```csharp
public record Schedule
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `ScheduleType` | `Cron` or `Interval` |
| `Interval` | `TimeSpan?` | The interval between executions (only for `ScheduleType.Interval`) |
| `CronExpression` | `string?` | The cron expression, 5-field or 6-field, evaluated in UTC (only for `ScheduleType.Cron`) |

### Factory Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `FromInterval` | `static Schedule FromInterval(TimeSpan interval)` | Creates an interval-based schedule. Throws `ArgumentOutOfRangeException` for an interval shorter than one second. |
| `FromCron` | `static Schedule FromCron(string expression)` | Creates a cron-based schedule. Parses the expression and throws `FormatException` when it is not a valid 5-field or 6-field cron. |

### ToCronExpression

```csharp
public string ToCronExpression()
```

Converts the schedule to a cron expression (5-field or 6-field). For cron-type schedules, returns the expression as-is. For interval-type schedules, converts to the closest valid cron expression. Sub-minute intervals produce 6-field (seconds) cron; minute-or-above intervals produce 5-field cron.

**Approximation**: Cron cannot express all intervals. Intervals that don't divide evenly into 60 minutes or 60 seconds are approximated to the nearest valid cron divisor of 60 (`1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30`).

### ScheduleType Enum

| Value | Description |
|-------|-------------|
| `None` | Manual-only; must be triggered via API |
| `Cron` | Runs on a cron expression schedule |
| `Interval` | Runs at a fixed time interval |
| `OnDemand` | Batch operations triggered programmatically |
| `Dependent` | Runs after a parent manifest completes successfully |
| `DormantDependent` | A dependent that must be explicitly activated at runtime via `IDormantDependentContext`. Never auto-fires. |
| `Once` | Fires once at `ScheduledAt`, then auto-disables on success. Created by [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management#scheduleonceasync). See [Delayed / One-Off Jobs](/docs/scheduler/delayed-jobs). |

### MisfirePolicy Enum

Determines behavior when a scheduled run is missed.

| Value | Description |
|-------|-------------|
| `FireOnceNow` | Fire once immediately if overdue. Default behavior. |
| `DoNothing` | If overdue beyond the misfire threshold, skip and wait for the next natural occurrence. |

See [Misfire Policies](/docs/scheduler/scheduling-options#misfire-policies) for detailed behavior and examples.

### ExclusionType Enum

Defines the kind of exclusion window for a manifest schedule. Used inside the JSONB `exclusions` column.

| Value | Description |
|-------|-------------|
| `DaysOfWeek` | Exclude specific days of the week (e.g., weekends) |
| `Dates` | Exclude specific dates (e.g., holidays) |
| `DateRange` | Exclude a contiguous date range (start–end inclusive) |
| `TimeWindow` | Exclude a daily time window (supports midnight crossover) |

See [Exclusion Windows](/docs/scheduler/exclusions) for usage patterns and examples.

---

## ManifestOptions

Per-item configuration passed to the `configureEach` callback of the batch scheduling methods (`ScheduleMany`, `IncludeMany`, `ThenIncludeMany`, `ScheduleManyAsync`, `ScheduleManyDependentAsync`), which receives each source item and its `ManifestOptions`. A single manifest, and settings shared by a whole batch, are configured through the [`ScheduleOptions`](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions) builder instead.

```csharp
public class ManifestOptions
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `true` | Whether the manifest is enabled. When `false`, ManifestManager skips it during polling, a dormant dependent is not activated, and the dispatcher holds the manifest's scheduled entries until it is re-enabled (a trigger or a dead-letter requeue still runs). Written to an existing manifest only when set, so a re-seed that leaves it unset keeps a runtime disable. See [Disabling a job](/docs/scheduler/scheduling-options#disabling-a-job). |
| `MaxRetries` | `int` | `DefaultMaxRetries` (3) | Retries after the first run before the job is dead-lettered (the default allows four attempts; `0` dead-letters on the first failure). Each retry creates a new Metadata record. Setting a negative value throws `ArgumentOutOfRangeException`. Unset, a new manifest takes the scheduler's `DefaultMaxRetries` and an existing one keeps its stored value at a re-seed; inside `configureEach` it reads the batch's resolved value. |
| `FailureWindow` | `TimeSpan?` | `null` | How far back this manifest's failed runs count toward its retry backoff and `MaxRetries`. `null` uses the scheduler's `FailureCountWindow`. Stored in whole seconds; throws `ArgumentOutOfRangeException` unless between one second and ten years. Written to an existing manifest only when stated, so a manifest that stops stating it keeps the window it has. Can also be set with `ScheduleOptions.FailureWindow(...)`. |
| `Timeout` | `TimeSpan?` | `null` | Per-job timeout override. `null` falls back to the global `DefaultJobTimeout`. Written to an existing manifest only when set (setting `null` states "use the global default"). A run that exceeds it is cancelled, and trains nested inside the run share it, and the stale in-progress reaper waits at least this long (plus its grace) before failing a run. |
| `Priority` | `int` | `0` | Manifest-level priority stored on the manifest record. Written to an existing manifest only when set; a new one takes 0. The dispatcher orders by **ManifestGroup.Priority** first, then by the entry's priority, and every entry for the manifest (scheduled, triggered or requeued) is queued at this priority. For dependent manifests, `DependentPriorityBoost` (default 16) is added on top at dispatch time. Can also be set with `ScheduleOptions.Priority(...)`. |
| `MisfirePolicy` | `MisfirePolicy?` | `null` | Per-manifest misfire policy override. `null` uses the global `DefaultMisfirePolicy`. Only applies to Cron and Interval schedule types. See [Misfire Policies](/docs/scheduler/scheduling-options#misfire-policies). |
| `MisfireThreshold` | `TimeSpan?` | `null` | Per-manifest misfire threshold override. `null` uses the global `DefaultMisfireThreshold` (60 seconds). |
| `Exclusions` | `List<Exclusion>` | `[]` | Exclusion windows for this manifest. When any exclusion matches the current time, the manifest is skipped. Excluded periods are "intentionally skipped", not misfires. See [Exclusion Windows](/docs/scheduler/exclusions). |

### Example

```csharp
// Per item, through configureEach on a batch...
await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, string>(
    tables,
    table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    Every.Minutes(5),
    configureEach: (table, opts) =>
    {
        opts.MaxRetries = table == "orders" ? 5 : 3;
        opts.Timeout = TimeSpan.FromMinutes(30);
        opts.Priority = 20;
    });

// ...or for a single manifest, through the ScheduleOptions builder
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job",
    new MyInput(),
    Every.Minutes(5),
    options => options.MaxRetries(5).Timeout(TimeSpan.FromMinutes(30)).Priority(20));
```
