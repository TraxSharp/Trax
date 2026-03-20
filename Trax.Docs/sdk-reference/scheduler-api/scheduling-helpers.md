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
Cron.Daily(hour: 3)                          // Daily at 3:00 AM
Cron.Daily(hour: 14, minute: 30)             // Daily at 2:30 PM
Cron.Weekly(DayOfWeek.Monday, hour: 9)       // Every Monday at 9:00 AM
Cron.Monthly(day: 15, hour: 0)               // 15th of each month at midnight
Cron.Expression("0 */6 * * *")              // Every 6 hours (custom cron)

// 6-field (second granularity)
Cron.Secondly()                              // Every second
Cron.Minutely(second: 30)                   // Every minute at :30 seconds
Cron.Hourly(minute: 15, second: 45)         // Every hour at 15:45
Cron.Daily(hour: 3, minute: 0, second: 30)  // Daily at 3:00:30 AM
Cron.Expression("*/15 * * * * *")           // Every 15 seconds (custom 6-field)
```

### Cron Expression Format

Trax supports both standard 5-field and 6-field (with seconds) cron formats. The format is auto-detected by counting fields.

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
| `CronExpression` | `string?` | The cron expression, 5-field or 6-field (only for `ScheduleType.Cron`) |

### Factory Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `FromInterval` | `static Schedule FromInterval(TimeSpan interval)` | Creates an interval-based schedule |
| `FromCron` | `static Schedule FromCron(string expression)` | Creates a cron-based schedule |

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

Per-job configuration passed via the `configure` callback in scheduling methods.

```csharp
public class ManifestOptions
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `true` | Whether the manifest is enabled. When `false`, ManifestManager skips it during polling. |
| `MaxRetries` | `int` | `3` | Maximum retry attempts before the job is dead-lettered. Each retry creates a new Metadata record. |
| `Timeout` | `TimeSpan?` | `null` | Per-job timeout override. `null` falls back to the global `DefaultJobTimeout`. If a job exceeds this duration, it may be considered stuck. |
| `Priority` | `int` | `0` | Manifest-level priority stored on the manifest record. Note: dispatch ordering is primarily determined by **ManifestGroup.Priority** (set from the dashboard). This manifest-level priority is used as the work queue entry's priority when the manifest is queued. For dependent manifests, `DependentPriorityBoost` (default 16) is added on top at dispatch time. Can also be set via the `priority` parameter on scheduling methods. |
| `MisfirePolicy` | `MisfirePolicy?` | `null` | Per-manifest misfire policy override. `null` uses the global `DefaultMisfirePolicy`. Only applies to Cron and Interval schedule types. See [Misfire Policies](/docs/scheduler/scheduling-options#misfire-policies). |
| `MisfireThreshold` | `TimeSpan?` | `null` | Per-manifest misfire threshold override. `null` uses the global `DefaultMisfireThreshold` (60 seconds). |
| `Exclusions` | `List<Exclusion>` | `[]` | Exclusion windows for this manifest. When any exclusion matches the current time, the manifest is skipped. Excluded periods are "intentionally skipped", not misfires. See [Exclusion Windows](/docs/scheduler/exclusions). |

### Example

```csharp
// Priority can be set via the configure callback...
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job",
    new MyInput(),
    Every.Minutes(5),
    configure: opts =>
    {
        opts.IsEnabled = true;
        opts.MaxRetries = 5;
        opts.Timeout = TimeSpan.FromMinutes(30);
        opts.Priority = 20;
    });

// ...or directly via the priority parameter (simpler for most cases)
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job",
    new MyInput(),
    Every.Minutes(5),
    priority: 20);
```
