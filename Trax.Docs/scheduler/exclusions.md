---
layout: default
title: Exclusion Windows
parent: Scheduling
nav_order: 8
---

# Exclusion Windows

{: .sdk-references }
> [Schedule](/docs/sdk-reference/scheduler-api/schedule)

Exclusion windows let you skip execution during specific periods — weekends, holidays, maintenance windows, or daily time ranges. When any exclusion matches the current time, the manifest is skipped. Excluded periods are treated as **intentionally skipped**, not as misfires.

## Exclusion Types

### DaysOfWeek

Skip specific days of the week:

```csharp
scheduler.Schedule<IReportTrain>(
    "daily-report",
    new ReportInput(),
    Cron.Daily(hour: 3),
    o => o.Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)));
```

### Dates

Skip specific dates (e.g., holidays):

```csharp
var holidays = new[]
{
    new DateOnly(2026, 12, 25),  // Christmas
    new DateOnly(2027, 1, 1),    // New Year's Day
};

scheduler.Schedule<IReportTrain>(
    "daily-report",
    new ReportInput(),
    Cron.Daily(hour: 3),
    o => o.Exclude(Exclude.Dates(holidays)));
```

### DateRange

Skip a contiguous date range (start and end inclusive):

```csharp
scheduler.Schedule<IReportTrain>(
    "daily-report",
    new ReportInput(),
    Cron.Daily(hour: 3),
    o => o.Exclude(Exclude.DateRange(
        new DateOnly(2026, 12, 23),
        new DateOnly(2027, 1, 2))));
```

### TimeWindow

Skip a daily time window. Supports midnight crossover (e.g., 23:00-02:00):

```csharp
scheduler.Schedule<ISyncTrain>(
    "sync-hourly",
    new SyncInput(),
    Cron.Hourly(),
    o => o.Exclude(Exclude.TimeWindow(
        TimeOnly.Parse("02:00"),
        TimeOnly.Parse("04:00"))));
```

## Combining Exclusions

Multiple exclusions can be combined. If **any** exclusion matches, the manifest is skipped:

```csharp
scheduler.Schedule<IReportTrain>(
    "weekday-report",
    new ReportInput(),
    Cron.Daily(hour: 8),
    o => o
        .Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday))
        .Exclude(Exclude.Dates(new DateOnly(2026, 12, 25)))
        .Exclude(Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00"))));
```

## Interaction with Misfire Policies

Excluded periods are **intentionally skipped** — they are not considered misfires. When the excluded period ends, normal scheduling resumes. If the manifest is overdue at that point, the existing misfire policy determines what happens:

- **`FireOnceNow`** (default): the manifest fires immediately to catch up
- **`DoNothing`**: the scheduler checks the most recent interval boundary and fires only if within the misfire threshold

This interaction is usually correct:
- A daily report excluded on weekends with `FireOnceNow` fires once on Monday morning to catch up
- The same report with `DoNothing` waits for the next natural 3am occurrence

## Storage

Exclusions are stored as a JSONB column on the manifest table. They are configured per-manifest via the `ScheduleOptions` fluent builder — there are no global exclusion defaults. Exclusions apply to all schedule types (Cron, Interval, Once). Dependent manifests are triggered by parent completion rather than time, so exclusions on dependent manifests are not evaluated during normal scheduling.

## Dashboard

The ManifestDetailPage displays configured exclusion windows in an "Exclusion Windows" card when present, showing the type and formatted description of each exclusion.

