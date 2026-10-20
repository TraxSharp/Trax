---
title: AddTraxWorker
description: Reference for AddTraxWorker, which runs a standalone worker process that polls the background_job table and executes trains, with LocalWorkerOptions.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 16
package: Trax.Scheduler
namespace: Trax.Scheduler.Extensions
source:
  - Trax.Scheduler/src/Trax.Scheduler/Extensions/WorkerExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/LocalWorkerOptions.cs
---

# AddTraxWorker

Registers a standalone worker: a process that polls the `background_job` table and runs the jobs in it, with no scheduling or dispatching of its own. It is the one method of `WorkerExtensions`.

## Signature

```csharp fragment
public static IServiceCollection AddTraxWorker(
    this IServiceCollection services,
    Action<LocalWorkerOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `configure` | `Action<LocalWorkerOptions>?` | `null` | Sets the worker count, polling interval and timeouts |

### LocalWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `Environment.ProcessorCount` | Number of concurrent worker tasks polling for jobs. 1 to 256 |
| `PollingInterval` | `TimeSpan` | 1 second | How often idle workers poll for new jobs. Greater than zero, up to 30 days |
| `VisibilityTimeout` | `TimeSpan` | 30 minutes | How long a claimed job stays invisible before another worker can reclaim it (crash recovery). 1 second to 10 years |
| `BatchSize` | `int` | `1` | Jobs each worker claims per poll. At least 1 |
| `ShutdownTimeout` | `TimeSpan` | 30 seconds | Grace period for in-flight jobs during shutdown. 0 to 30 days |

The same options as [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers), with the same ranges.

## Returns

The `IServiceCollection`, for chaining.

## Behavior

- `AddTraxWorker` throws `InvalidOperationException` listing every option outside its range.
- The scheduler that fills `background_job` for this process registers its submitter with `OverrideSubmitter(s => s.AddScoped<IJobSubmitter, PostgresJobSubmitter>())`; otherwise it starts local workers of its own that compete with this one (see [Remote Execution](/docs/scheduler/remote-execution)).

### Registered services

`AddTraxWorker()` calls `AddTraxJobRunner()` and adds the worker service:

| Service | Lifetime | Description |
|---------|----------|-------------|
| All services from `AddTraxJobRunner()` | _(various)_ | Execution pipeline (JobRunnerTrain, CancellationRegistry, etc.) |
| `LocalWorkerOptions` | Singleton | Worker configuration |
| `LocalWorkerService` | Hosted Service | Background worker that polls `background_job` and executes trains |

Not registered: the ManifestManager, the JobDispatcher, the polling services and the startup service.

A scheduler host whose `AddScheduler` already runs local workers (the default on Postgres) refuses `AddTraxWorker()`, in either order, with an `InvalidOperationException`: two sets of `LocalWorkerOptions` would silently replace one another. Configure the scheduler's pool with `AddScheduler(scheduler => scheduler.ConfigureLocalWorkers(o => ...))` instead.

### Compared with the scheduler's local workers

| Aspect | Scheduler (with Postgres) | `AddTraxWorker()` |
|--------|---------------------------|-------------------|
| **Used in** | Full scheduler process | Standalone worker process |
| **Scheduling** | Yes (ManifestManager, JobDispatcher) | No |
| **Dispatching** | Yes (writes to WorkQueue, background_job) | No |
| **Execution** | Yes (LocalWorkerService) | Yes (LocalWorkerService) |
| **Job submitter** | Registers `PostgresJobSubmitter` | Does not register any submitter |

### Several worker processes

Any number of worker processes may poll one database. Jobs are claimed with `FOR UPDATE SKIP LOCKED`, so each job is claimed by exactly one worker, with no coordination between processes.

```
┌── Worker Process A ──┐    ┌── Worker Process B ──┐
│  4 worker tasks       │    │  4 worker tasks       │
│  polling same table   │    │  polling same table   │
└───────────┬───────────┘    └───────────┬───────────┘
            │                            │
            └──────────┬─────────────────┘
                       ▼
              background_job table
              (SKIP LOCKED ensures
               no duplicate claims)
```

### Shared requirements

A worker process:

- References the train assemblies the scheduler passes to `AddMediator()`. Train types are resolved by fully-qualified name.
- Connects to the scheduler's Postgres database. Metadata, manifests, and state are shared across all processes.
- Registers the effects with `AddTrax()` and `UsePostgres()`.

## Examples

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(ISyncTrain).Assembly)
);
builder.Services.AddTraxWorker();

var app = builder.Build();
app.Run();
```

A worker whose assemblies hold `[TraxAuthorize]` trains does not start unless the mediator is told the process takes no submissions, since a worker has no `ITrainAuthorizationService`; the caller was checked when the job was queued:

```csharp compile
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(ISyncTrain).Assembly)
        .AllowMissingAuthorizationService()));
builder.Services.AddTraxWorker();
```

See [Authorization: Opting Out for Scheduler-Only Hosts](/docs/authorization#opting-out-for-scheduler-only-hosts). The [Energy Hub sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers) runs this way.

Worker options:

```csharp compile
builder.Services.AddTraxWorker(opts =>
{
    opts.WorkerCount = 4;
    opts.PollingInterval = TimeSpan.FromSeconds(2);
    opts.VisibilityTimeout = TimeSpan.FromMinutes(15);
    opts.ShutdownTimeout = TimeSpan.FromMinutes(1);
});
```

## See also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers): customizing local workers within the scheduler process
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): push-based alternative (HTTP endpoint)
