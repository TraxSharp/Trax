---
layout: default
title: UseHangfire
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 2
---

# UseHangfire (Deprecated)

> **Deprecated**: Use [`UsePostgresTaskServer()`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-postgres-task-server.md %}) instead. The Hangfire package will be removed in a future version.

Configures [Hangfire](https://www.hangfire.io/) as the background task server for the Trax.Core scheduler, using PostgreSQL for job storage.

## Signature

```csharp
public static SchedulerConfigurationBuilder UseHangfire(
    this SchedulerConfigurationBuilder builder,
    string connectionString
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connectionString` | `string` | Yes | PostgreSQL connection string for Hangfire's job storage |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(assemblies: typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseHangfire(connectionString)
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

## Remarks

- Hangfire's automatic retries are **disabled** — Trax.Core manages retries through the manifest system (`DefaultMaxRetries`, `RetryBackoffMultiplier`, etc.).
- Completed Hangfire jobs are auto-deleted to prevent storage bloat.
- The `InvisibilityTimeout` is set to 30 minutes (above the default `DefaultJobTimeout` of 20 minutes) to prevent Hangfire from re-enqueuing long-running jobs that Trax.Core is still tracking.
- Requires the `Trax.Scheduler.Hangfire` NuGet package.

## Package

```
dotnet add package Trax.Scheduler.Hangfire
```
