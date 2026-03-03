---
layout: default
title: Setup
parent: Scheduling
nav_order: 1
---

# Setup & Creating Scheduled Trains

## Quick Setup

### Installation

```bash
dotnet add package Trax.Scheduler
```

The scheduler includes a built-in PostgreSQL task server — no additional packages needed.

### Configuration

Jobs can be scheduled directly in startup configuration. The scheduler creates or updates manifests when the app starts:

```csharp
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.TaskServerExecutor;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Database");

builder.Services.AddTrax.CoreEffects(options => options
    .AddServiceTrainBus(
        typeof(Program).Assembly,
        typeof(TaskServerExecutorTrain).Assembly  // Required — see note below
    )
    .AddPostgresEffect(connectionString)
    .AddScheduler(scheduler => scheduler
        .PollingInterval(TimeSpan.FromSeconds(5))
        .MaxActiveJobs(100)
        .DefaultMaxRetries(3)
        .UsePostgresTaskServer()

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

*SDK Reference: [AddScheduler]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-scheduler.md %}), [UsePostgresTaskServer]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-postgres-task-server.md %}), [Schedule]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %})*

`AddScheduler` registers three hosted services — `SchedulerStartupService` (seeds manifests and recovers stuck jobs on startup), `ManifestManagerPollingService` (evaluates manifests on a timer), and `JobDispatcherPollingService` (dispatches work queue entries on a timer). No extra startup call needed.

`UsePostgresTaskServer()` starts a background worker service that polls the `trax.background_job` table for queued jobs using PostgreSQL's `FOR UPDATE SKIP LOCKED` for atomic, lock-free dequeue. No extra connection string needed — it reuses the `IDataContext` from `AddPostgresEffect()`. See [Task Server]({{ site.baseurl }}{% link scheduler/task-server.md %}) for architecture details.

> **`TaskServerExecutorTrain.Assembly` is required.** The `TrainBus` discovers trains by scanning assemblies. `TaskServerExecutorTrain` is the internal train that the task server invokes when a job fires—if its assembly isn't registered, scheduled jobs will silently fail to execute with no error message. Always include it alongside your own assemblies.

### Task Server Options

You can customize the task server's worker count, polling interval, and timeouts:

```csharp
.UsePostgresTaskServer(options =>
{
    options.WorkerCount = 4;                                // default: processor count
    options.PollingInterval = TimeSpan.FromSeconds(2);      // default: 1 second
    options.VisibilityTimeout = TimeSpan.FromMinutes(15);   // default: 30 minutes
    options.ShutdownTimeout = TimeSpan.FromMinutes(1);      // default: 30 seconds
})
```

See [UsePostgresTaskServer]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-postgres-task-server.md %}) for full parameter documentation.

> **Migrating from Hangfire?** See the [migration guide]({{ site.baseurl }}{% link scheduler/task-server.md %}#migrating-from-hangfire).

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
            .Chain<FetchCustomersStep>()
            .Chain<TransformDataStep>()
            .Chain<WriteToDestinationStep>()
            .Resolve();
}
```

### 3. Schedule It

**Option A: Startup Configuration (recommended for static jobs)**

```csharp
.AddScheduler(scheduler => scheduler
    .UsePostgresTaskServer()
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
        await scheduler.ScheduleAsync<ISyncCustomersTrain, SyncCustomersInput>(
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

*SDK Reference: [Scheduling Helpers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/scheduling-helpers.md %}) — all method signatures, cron expression format, and `ManifestOptions`.*

## Namespace Reference

The scheduler spans multiple packages. This table lists every public type you're likely to use during integration:

| Type | Namespace | Package |
|------|-----------|---------|
| `IManifestProperties` | `Trax.Effect.Models.Manifest` | `Trax.Effect` |
| `TaskServerExecutorTrain` | `Trax.Scheduler.Trains.TaskServerExecutor` | `Trax.Scheduler` |
| `PostgresTaskServerOptions` | `Trax.Scheduler.Configuration` | `Trax.Scheduler` |
| `Cron` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `Every` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `Schedule` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `ITraxScheduler` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `ManifestOptions` | `Trax.Scheduler.Configuration` | `Trax.Scheduler` |
| `IDormantDependentContext` | `Trax.Scheduler.Services.DormantDependentContext` | `Trax.Scheduler` |
