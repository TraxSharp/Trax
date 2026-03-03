---
layout: default
title: UsePostgresTaskServer
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 2
---

# UsePostgresTaskServer

Configures the built-in PostgreSQL task server as the background execution backend for the Trax.Core scheduler.

## Signature

```csharp
public SchedulerConfigurationBuilder UsePostgresTaskServer(
    Action<PostgresTaskServerOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<PostgresTaskServerOptions>?` | No | Optional callback to customize worker count, polling interval, and timeouts |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## PostgresTaskServerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `Environment.ProcessorCount` | Number of concurrent worker tasks polling for jobs |
| `PollingInterval` | `TimeSpan` | 1 second | How often idle workers poll for new jobs |
| `VisibilityTimeout` | `TimeSpan` | 30 minutes | How long a claimed job stays invisible before another worker can reclaim it (crash recovery) |
| `ShutdownTimeout` | `TimeSpan` | 30 seconds | Grace period for in-flight jobs during shutdown |

## Examples

### Basic Usage (Defaults)

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(assemblies: typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

### Custom Configuration

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(assemblies: typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer(options =>
        {
            options.WorkerCount = 4;
            options.PollingInterval = TimeSpan.FromSeconds(2);
            options.VisibilityTimeout = TimeSpan.FromMinutes(15);
            options.ShutdownTimeout = TimeSpan.FromMinutes(1);
        })
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

## Remarks

- No connection string parameter is needed. `UsePostgresTaskServer()` uses the same `IDataContext` registered by `AddPostgresEffect()`.
- No additional NuGet packages required — this is included in `Trax.Scheduler`.
- Jobs are queued to the `trax.background_job` table and dequeued atomically using PostgreSQL's `FOR UPDATE SKIP LOCKED`.
- Workers delete job rows after execution (both success and failure). Trax.Core's Metadata and DeadLetter tables handle the audit trail.
- If a worker crashes mid-execution, the job's `fetched_at` timestamp becomes stale and the job is reclaimed after `VisibilityTimeout`.

## Registered Services

`UsePostgresTaskServer()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `PostgresTaskServerOptions` | Singleton | Configuration options |
| `IBackgroundTaskServer` → `PostgresTaskServer` | Scoped | Enqueue implementation (INSERT into background_job) |
| `PostgresWorkerService` | Hosted Service | Background worker that polls and executes jobs |

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Task Server Architecture]({{ site.baseurl }}{% link scheduler/task-server.md %}) — detailed architecture, crash recovery, comparison with Hangfire
- [UseHangfire]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-hangfire.md %}) (deprecated)
