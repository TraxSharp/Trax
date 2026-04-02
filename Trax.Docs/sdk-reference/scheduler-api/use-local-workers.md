---
layout: default
title: ConfigureLocalWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 2
---

# ConfigureLocalWorkers

Customizes the built-in PostgreSQL local worker pool. Local workers are registered automatically when `UsePostgres()` is configured. No explicit call is needed to enable them.

## Signature

```csharp
public SchedulerConfigurationBuilder ConfigureLocalWorkers(
    Action<LocalWorkerOptions> configure
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<LocalWorkerOptions>` | Yes | Callback to customize worker count, polling interval, and timeouts |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## LocalWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `Environment.ProcessorCount` | Number of concurrent worker tasks polling for jobs |
| `PollingInterval` | `TimeSpan` | 1 second | How often idle workers poll for new jobs |
| `VisibilityTimeout` | `TimeSpan` | 30 minutes | How long a claimed job stays invisible before another worker can reclaim it (crash recovery) |
| `BatchSize` | `int` | `1` | Number of jobs each worker claims per poll cycle |
| `ShutdownTimeout` | `TimeSpan` | 30 seconds | Grace period for in-flight jobs during shutdown |

## Examples

### Default Behavior (No Call Needed)

When `UsePostgres()` is configured, local workers are enabled automatically with default settings. You don't need to call anything to get local execution:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

### Custom Configuration

Use `ConfigureLocalWorkers()` to customize worker behavior:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .ConfigureLocalWorkers(options =>
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

### Mixed Local and Remote Workers

Local workers are always registered alongside remote submitters. Trains not routed to a remote submitter execute locally:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .ConfigureLocalWorkers(opts => opts.WorkerCount = 8)
        .UseRemoteWorkers(
            remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
            routing => routing
                .ForTrain<IHeavyComputeTrain>()
                .ForTrain<IAiInferenceTrain>())
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
        .Schedule<IHeavyComputeTrain, HeavyInput>("heavy-compute", new HeavyInput(), Every.Hours(1))
    )
);
```

In this example, `IHeavyComputeTrain` is dispatched to the remote HTTP endpoint, while `IMyTrain` executes locally with 8 worker threads.

## Remarks

- Local workers are the **implicit default** when `UsePostgres()` is configured. You only need `ConfigureLocalWorkers()` if you want to change the defaults.
- No connection string parameter is needed. Local workers use the same `IDataContext` registered by `UsePostgres()`.
- No additional NuGet packages required. This is included in `Trax.Scheduler`.
- Jobs are queued to the `trax.background_job` table and dequeued atomically using PostgreSQL's `FOR UPDATE SKIP LOCKED`.
- Workers delete job rows after execution (both success and failure). Trax.Core's Metadata and DeadLetter tables handle the audit trail.
- If a worker crashes mid-execution, the job's `fetched_at` timestamp becomes stale and the job is reclaimed after `VisibilityTimeout`.
- When `UseRemoteWorkers()` or `UseSqsWorkers()` is also configured, local workers still run. Only the trains explicitly routed via `ForTrain<T>()` or `[TraxRemote]` are dispatched remotely.

## Registered Services

The scheduler automatically registers these services when `UsePostgres()` is configured:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `LocalWorkerOptions` | Singleton | Configuration options |
| `IJobRunnerTrain` → `JobRunnerTrain` | Scoped | The train that workers use to execute each job |
| `IJobSubmitter` → `PostgresJobSubmitter` | Scoped | Enqueue implementation (INSERT into background_job) |
| `LocalWorkerService` | Hosted Service | Background worker that polls and executes jobs |

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Job Submission Architecture](/docs/scheduler/job-submission): detailed architecture, crash recovery, comparison with Hangfire
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): per-train HTTP remote dispatch
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers): per-train SQS dispatch
