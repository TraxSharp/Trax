---
layout: default
title: Setup
parent: Scheduling
nav_order: 1
---

# Setup & Creating Scheduled Trains

{: .sdk-references }
> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers)

## Quick Setup

### Installation

```bash
dotnet add package Trax.Scheduler
```

The scheduler includes built-in local workers backed by PostgreSQL — no additional packages needed.

### Default Job Submitter

The scheduler automatically selects the right job submitter based on your effect configuration:

| Effect Configuration | Job Submitter | Behavior |
|---------------------|---------------|----------|
| `UsePostgres(...)` | `PostgresJobSubmitter` | Inserts into `trax.background_job` table. Local workers are started automatically. |
| `UseInMemory()` (no database) | `InMemoryJobSubmitter` | Executes jobs inline, synchronously. No database needed. Good for testing and prototyping. |
| `OverrideSubmitter(...)` | Custom | Your own `IJobSubmitter` implementation takes priority over both defaults. |

> **Validation:** The scheduler validates configuration at build time. `AddScheduler()` requires a data provider (`UsePostgres()` or `UseInMemory()`) — without one, it throws a clear `InvalidOperationException` with a message showing the fix. Similarly, `AddJunctionProgress()` without a data provider fails fast at build time.

### Configuration

Jobs can be scheduled directly in startup configuration. The scheduler creates or updates manifests when the app starts:

```csharp
using Trax.Scheduler.Services.TraxScheduler;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Database");

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .PollingInterval(TimeSpan.FromSeconds(5))
        .MaxActiveJobs(10)
        .DefaultMaxRetries(3)

        // Schedule jobs directly in configuration
        .Schedule<IHelloWorldTrain, HelloWorldInput>(
            "hello-world",
            new HelloWorldInput { Name = "Trax.Core Scheduler" },
            Every.Minutes(1))

        .Schedule<IDailyReportTrain, DailyReportInput>(
            "daily-report",
            new DailyReportInput { ReportType = "sales" },
            Cron.Daily(hour: 3),
            opts => opts.MaxRetries = 5)
    )
);

var app = builder.Build();
app.Run();
```

`AddScheduler` registers hosted services based on the configured data provider:

| Service | PostgreSQL | InMemory |
|---------|-----------|----------|
| `SchedulerStartupService` | Seeds manifests, recovers stuck jobs | Seeds manifests |
| `ManifestManagerPollingService` | Evaluates manifests on a timer, creates work queue entries | Evaluates manifests on a timer, dispatches jobs inline |
| `JobDispatcherPollingService` | Claims work queue entries via `FOR UPDATE SKIP LOCKED` | Not registered (InMemory dispatches directly) |
| `MetadataCleanupPollingService` | Cleans up old metadata (if `AddMetadataCleanup()`) | Not registered (uses `ExecuteDeleteAsync`) |

With InMemory, the `ManifestManagerPollingService` runs an `InMemoryManifestManagerTrain` that skips PostgreSQL-specific junctions (`CancelTimedOutJobs`, `ReapStalePending`) and dispatches jobs directly via `InMemoryJobSubmitter` — no work queue or JobDispatcher needed.

When `UsePostgres()` is configured, the scheduler automatically starts a background worker service that polls the `trax.background_job` table for queued jobs using PostgreSQL's `FOR UPDATE SKIP LOCKED` for atomic, lock-free dequeue. No extra connection string needed — it reuses the `IDataContext` from `UsePostgres()`. See [Job Submission](/docs/scheduler/job-submission) for architecture details.

All internal scheduler trains (`ManifestManagerTrain`, `JobDispatcherTrain`, `JobRunnerTrain`, `MetadataCleanupTrain`) are registered automatically by `AddScheduler()` — you only need to pass your own train assemblies to `AddMediator()`.

### Local Worker Options

You can customize the local workers' worker count, polling interval, and timeouts with `ConfigureLocalWorkers()`:

```csharp
.ConfigureLocalWorkers(options =>
{
    options.WorkerCount = 4;                                // default: processor count
    options.PollingInterval = TimeSpan.FromSeconds(2);      // default: 1 second
    options.VisibilityTimeout = TimeSpan.FromMinutes(15);   // default: 30 minutes
    options.ShutdownTimeout = TimeSpan.FromMinutes(1);      // default: 30 seconds
})
```

See [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) for full parameter documentation.

> **Migrating from Hangfire?** See the [migration guide](/docs/scheduler/job-submission#migrating-from-hangfire).

## Creating Scheduled Trains

### 1. Define the Input

Your train input must implement `IManifestProperties`. This marker interface signals the type is safe for serialization and storage:

```csharp
using Trax.Effect.Models.Manifest;

public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
    public int BatchSize { get; init; } = 1000;
}
```

Types without `IManifestProperties` won't compile with the scheduling API—this catches mistakes before runtime.

`IManifestProperties` lives in the `Trax.Effect` package (namespace `Trax.Effect.Models.Manifest`), not in the Scheduler package. You won't need an extra package reference if you already have `Trax.Effect` installed.

### 2. Create the Train

Standard `ServiceTrain` with an interface for DI resolution:

```csharp
public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, Unit> { }

public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SyncCustomersInput input)
        => Activate(input)
            .Chain<FetchCustomersJunction>()
            .Chain<TransformDataJunction>()
            .Chain<WriteToDestinationJunction>()
            .Resolve();
}
```

Scheduled trains can return any output type — the output is discarded for background jobs. Using `Unit` is common for fire-and-forget work, but any `TOutput` is valid:

```csharp
// A train that returns a result type — the output is discarded by the scheduler
public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, SyncResult> { }

public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, SyncResult>, ISyncCustomersTrain
{
    protected override async Task<Either<Exception, SyncResult>> RunInternal(SyncCustomersInput input)
        => Activate(input)
            .Chain<FetchCustomersJunction>()
            .Chain<TransformDataJunction>()
            .Chain<WriteToDestinationJunction>()
            .Resolve();
}
```

### 3. Schedule It

**Option A: Startup Configuration (recommended for static jobs)**

```csharp
.AddScheduler(scheduler => scheduler
    .Schedule<ISyncCustomersTrain, SyncCustomersInput>(
        "sync-customers-us-east",
        new SyncCustomersInput { Region = "us-east", BatchSize = 500 },
        Cron.Hourly(minute: 30),
        opts => opts.MaxRetries = 3)
)
```

**Option B: Runtime via ITraxScheduler (for dynamic jobs)**

```csharp
public class JobSetupService(ITraxScheduler scheduler)
{
    public async Task SetupJobs()
    {
        await scheduler.ScheduleAsync<ISyncCustomersTrain, SyncCustomersInput, Unit>(
            "sync-customers-us-east",
            new SyncCustomersInput { Region = "us-east", BatchSize = 500 },
            Every.Hours(6),
            opts => opts.MaxRetries = 3);
    }
}
```

Both approaches use upsert semantics—the ExternalId determines whether to create or update the manifest.

## Schedule Helpers

The `Schedule` type defines when a job runs. Two static factory classes create `Schedule` objects:

- **`Every`** — interval-based: `Every.Seconds(30)`, `Every.Minutes(5)`, `Every.Hours(1)`, `Every.Days(1)`
- **`Cron`** — cron-based: `Cron.Minutely()`, `Cron.Daily(hour: 3)`, `Cron.Weekly(DayOfWeek.Sunday, hour: 2)`, `Cron.Expression("0 */6 * * *")`

## Namespace Reference

The scheduler spans multiple packages. This table lists every public type you're likely to use during integration:

| Type | Namespace | Package |
|------|-----------|---------|
| `IManifestProperties` | `Trax.Effect.Models.Manifest` | `Trax.Effect` |
| `JobRunnerTrain` | `Trax.Scheduler.Trains.JobRunner` | `Trax.Scheduler` |
| `LocalWorkerOptions` | `Trax.Scheduler.Configuration` | `Trax.Scheduler` |
| `Cron` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `Every` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `Schedule` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `ITraxScheduler` | `Trax.Scheduler.Services.TraxScheduler` | `Trax.Scheduler` |
| `ManifestOptions` | `Trax.Scheduler.Configuration` | `Trax.Scheduler` |
| `IDormantDependentContext` | `Trax.Scheduler.Services.DormantDependentContext` | `Trax.Scheduler` |
