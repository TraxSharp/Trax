---
title: ConfigureLocalWorkers
description: Reference for ConfigureLocalWorkers, which tunes the built-in local worker pool that a database provider enables by default, with LocalWorkerOptions.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 3
package: Trax.Scheduler
namespace: Trax.Scheduler.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.Settings.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/LocalWorkerOptions.cs
redirect_from:
  - /docs/sdk-reference/scheduler-api/use-local-workers
---

# ConfigureLocalWorkers

Configures the built-in local worker pool, which runs queued jobs in the scheduler's own process. The pool is registered whenever the scheduler has a database provider, `UsePostgres()` or `UseSqlite()`, whether or not this is called.

## Signature

```csharp fragment
public SchedulerConfigurationBuilder ConfigureLocalWorkers(
    Action<LocalWorkerOptions> configure
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<LocalWorkerOptions>` | Sets the worker count, polling interval and timeouts |

### LocalWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `Environment.ProcessorCount`, or `1` on SQLite | Number of concurrent worker tasks polling for jobs. 1 to 256. A value you set is used on either provider |
| `PollingInterval` | `TimeSpan` | 1 second | How often idle workers poll for new jobs. Greater than zero, up to 30 days |
| `VisibilityTimeout` | `TimeSpan` | 30 minutes | How long a claimed job stays invisible after its worker last refreshed its claim before another worker can reclaim it (crash recovery). A running job's claim is refreshed every third of this, so a long job is not reclaimed while it runs |
| `BatchSize` | `int` | `1` | Number of jobs each worker claims per poll cycle. At least 1. Every job in the batch keeps its claim until the worker runs it, however long the jobs ahead of it take |
| `ShutdownTimeout` | `TimeSpan` | 30 seconds | Grace period for in-flight jobs during shutdown. 0 to 30 days |

## Returns

`SchedulerConfigurationBuilder`, for chaining.

## Behavior

- Local workers use the `IDataContext` the database provider registers.
- Jobs are queued to the `trax.background_job` table. On Postgres they are dequeued with `FOR UPDATE SKIP LOCKED`, so workers in any number of processes claim without blocking each other.
- On SQLite each claim runs under `BEGIN IMMEDIATE`, which takes the file's write lock. Workers queue on that lock rather than running side by side, so the pool starts one worker unless you set `WorkerCount`, and more do not help.
- Workers delete job rows after execution (both success and failure), including a job that finishes during shutdown: the delete does not take the host's stopping token. Trax.Core's Metadata and DeadLetter tables handle the audit trail.
- `AddScheduler` refuses options outside their ranges when it is built (see [Value Ranges](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges)); `VisibilityTimeout` must be between one second and ten years.
- While a worker holds jobs, it refreshes the `fetched_at` of every job it has claimed and not yet finished, the running one and any waiting behind it in a batch, every third of `VisibilityTimeout`. If a worker crashes mid-execution, the refreshes stop, the timestamp becomes stale, and the job is reclaimed after `VisibilityTimeout`.
- A job waiting in `trax.background_job` for a free worker is not failed by the stale-pending reaper, however long it waits.
- A job that fails before its run starts (its input cannot be read, or the run's start cannot be written) is deleted like any other, and the worker fails the run itself, still `Pending`, with the reason "The job failed before its run started". The failure goes through the lifecycle hooks at once, so a machine that invoked the run reaches its `OnFailed` target without waiting for the stale-pending reaper. The worker's log has the error. A run that started records its own outcome.
- With `UseRemoteWorkers()`, `UseSqsWorkers()` or `UseLambdaWorkers()` also configured, local workers still run every train those calls do not route.

### Registered services

With a database provider, the scheduler registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `LocalWorkerOptions` | Singleton | Configuration options |
| `IJobRunnerTrain` → `JobRunnerTrain` | Scoped | The train that workers use to execute each job |
| `IJobSubmitter` → `PostgresJobSubmitter` | Scoped | Enqueue implementation (INSERT into background_job) |
| `LocalWorkerService` | Hosted Service | Background worker that polls and executes jobs |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .ConfigureLocalWorkers(options =>
        {
            options.WorkerCount = 4;
            options.PollingInterval = TimeSpan.FromSeconds(2);
            options.VisibilityTimeout = TimeSpan.FromMinutes(15);
            options.ShutdownTimeout = TimeSpan.FromMinutes(1);
        })
        .UseRemoteWorkers(
            remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
            routing => routing.ForTrain<IReviewContentTrain>())
        .Schedule<ISyncTrain>("sync", new SyncInput(), Every.Minutes(5))));
```

`IReviewContentTrain` is sent to the remote endpoint; `ISyncTrain` runs on the four local workers.

## See also

- [Job Submission Architecture](/docs/scheduler/job-submission): detailed architecture, crash recovery, comparison with Hangfire
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): per-train HTTP remote dispatch
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers): per-train SQS dispatch
