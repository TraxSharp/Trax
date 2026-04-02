---
layout: default
title: AddTraxWorker
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 11
---

# AddTraxWorker

Registers a standalone worker process that polls the `background_job` table and executes trains. No scheduler logic, just execution.

## Signature

```csharp
public static IServiceCollection AddTraxWorker(
    this IServiceCollection services,
    Action<LocalWorkerOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<LocalWorkerOptions>?` | No | Optional callback to customize worker count, polling interval, and timeouts |

## LocalWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `Environment.ProcessorCount` | Number of concurrent worker tasks polling for jobs |
| `PollingInterval` | `TimeSpan` | 1 second | How often idle workers poll for new jobs |
| `VisibilityTimeout` | `TimeSpan` | 30 minutes | How long a claimed job stays invisible before another worker can reclaim it (crash recovery) |
| `ShutdownTimeout` | `TimeSpan` | 30 seconds | Grace period for in-flight jobs during shutdown |

These are the same options used by [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers).

## Examples

### Basic Standalone Worker

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxWorker();

var app = builder.Build();
app.Run();
```

### Custom Worker Configuration

```csharp
builder.Services.AddTraxWorker(opts =>
{
    opts.WorkerCount = 4;
    opts.PollingInterval = TimeSpan.FromSeconds(2);
    opts.VisibilityTimeout = TimeSpan.FromMinutes(15);
    opts.ShutdownTimeout = TimeSpan.FromMinutes(1);
});
```

### Multiple Worker Processes

You can run multiple standalone worker processes against the same database. PostgreSQL's `FOR UPDATE SKIP LOCKED` guarantees each job is claimed by exactly one worker. No duplicates, no coordination needed.

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

## What It Registers

`AddTraxWorker()` internally calls `AddTraxJobRunner()` and adds the worker service:

| Service | Lifetime | Description |
|---------|----------|-------------|
| All services from `AddTraxJobRunner()` | _(various)_ | Execution pipeline (JobRunnerTrain, CancellationRegistry, etc.) |
| `LocalWorkerOptions` | Singleton | Worker configuration |
| `LocalWorkerService` | Hosted Service | Background worker that polls `background_job` and executes trains |

**Not registered:** ManifestManager, JobDispatcher, polling services, startup service. This process only executes; it doesn't schedule or dispatch.

## How It Differs from the Scheduler's Local Workers

| Aspect | Scheduler (with Postgres) | `AddTraxWorker()` |
|--------|---------------------------|-------------------|
| **Used in** | Full scheduler process | Standalone worker process |
| **Scheduling** | Yes (ManifestManager, JobDispatcher) | No |
| **Dispatching** | Yes (writes to WorkQueue, background_job) | No |
| **Execution** | Yes (LocalWorkerService) | Yes (LocalWorkerService) |
| **Job submitter** | Registers `PostgresJobSubmitter` | Does not register any submitter |

## Shared Requirements

The standalone worker must:

- **Reference the same train assemblies** passed to `AddMediator()`. Train types are resolved by fully-qualified name.
- **Connect to the same Postgres database.** Metadata, manifests, and state are shared across all processes.
- **Register the effect system.** `AddTrax()` with `UsePostgres()` is required.

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers): customizing local workers within the scheduler process
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): push-based alternative (HTTP endpoint)
