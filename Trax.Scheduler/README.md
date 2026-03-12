# Trax.Scheduler

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler)](https://www.nuget.org/packages/Trax.Scheduler/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Timetable management for [Trax](https://www.nuget.org/packages/Trax.Effect/) trains — recurring schedules, automatic retries, dead-letter handling, and dependent departures.

## What This Does

If you have trains that need to run on a timetable — ETL pipelines, data syncs, nightly reports, periodic cleanup — Trax.Scheduler handles the dispatch. You write a manifest for each train (what cargo it carries, when it departs, how many times to retry if it derails), and the scheduler takes care of the rest.

Every scheduled run is a normal train journey, so you get the same journey logging, station services, and control room visibility as any other train.

## Installation

```bash
dotnet add package Trax.Scheduler
```

You'll also need a storage depot for persistent scheduling:

```bash
dotnet add package Trax.Effect.Data.Postgres
```

## Setup

Add the scheduler inside your `AddTrax` configuration:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).SaveTrainParameters().AddStepProgress()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IGenerateReportTrain>(
                "nightly-report",
                new GenerateReportInput { Format = "pdf" },
                Cron.Daily(hour: 3)
            )
        )
);
```

## Writing Manifests

A **manifest** describes a scheduled train: which service to run, what cargo it carries, and when it departs. Just like a shipping manifest lists what's on board and where it's going.

### Interval-based departures

```csharp
scheduler.Schedule<IHealthCheckTrain>(
    "health-check",
    new HealthCheckInput(),
    Every.Minutes(5)
);
```

### Cron-based departures

```csharp
scheduler.Schedule<ISyncCustomersTrain>(
    "sync-customers",
    new SyncCustomersInput { Source = "crm" },
    Cron.Hourly(minute: 0)
);
```

Available cron helpers: `Cron.Minutely()`, `Cron.Hourly()`, `Cron.Daily()`, `Cron.Weekly()`, `Cron.Monthly()`, and `Cron.Expression("...")` for arbitrary cron strings.

### Retry policy

```csharp
scheduler.Schedule<IImportDataTrain>(
    "import-data",
    new ImportDataInput(),
    Every.Hours(1),
    options: o => o.MaxRetries(5)
);
```

A train that derails gets re-dispatched up to `MaxRetries` times. If it keeps failing, the manifest moves to the **dead-letter queue** — the lost shipment office where undeliverable work sits until someone investigates.

## Fleet Scheduling

Dispatch a fleet of the same train type with different cargo:

```csharp
scheduler.ScheduleMany<IExtractTrain>(
    "extract",
    Enumerable.Range(0, 10).Select(i =>
        new ManifestItem($"{i}", new ExtractInput { TableIndex = i })),
    Every.Minutes(5)
);
```

This creates 10 manifests (`extract-0` through `extract-9`), each departing on the same interval with different cargo.

## Connected Departures

Schedule trains so that one departs only after another arrives:

```csharp
scheduler
    .Schedule<IExtractTrain>(
        "extract",
        new ExtractInput(),
        Every.Hours(1)
    )
    .Include<ITransformTrain>(
        "transform",
        new TransformInput()
    );
```

`transform` departs automatically when `extract` arrives successfully. You can chain further with `.ThenInclude<T>()`, or fan out with `.IncludeMany<T>()` and `.ThenIncludeMany<T>()` for fleet-scale dependent scheduling.

### Trains waiting in the yard

Sometimes a dependent train should only depart conditionally. Mark it as dormant — it sits in the yard, ready to go, waiting for a signal:

```csharp
// In scheduler config
scheduler
    .Schedule<IExtractTrain>("extract", input, Every.Hours(1))
    .IncludeMany<IQualityCheckTrain>(
        "quality",
        items,
        options: o => o.Dormant()
    );

// In a step, when you decide it's needed — signal the departure
public class CheckDataStep(IDormantDependentContext dormants) : Step<ExtractInput, Unit>
{
    public override async Task<Unit> Run(ExtractInput input)
    {
        if (anomaliesDetected)
        {
            await dormants.ActivateAsync<IQualityCheckTrain, QualityCheckInput>(
                "quality-0",
                new QualityCheckInput { /* ... */ }
            );
        }

        return Unit.Default;
    }
}
```

## How the Yard Works

The scheduler runs as three internal trains — all visible in the control room with full journey logging:

1. **ManifestManager** — the yard master. Polls on an interval, checks which manifests are due, and queues departures.
2. **JobDispatcher** — the dispatcher. Reads the departure queue, respects per-line capacity limits, and assigns trains to the track.
3. **TaskServerExecutor** — the engineer. Picks up an assigned train and drives it through its route.

## Journey Log Cleanup

Long-running timetables accumulate journey records. Configure automatic archival per train type:

```csharp
scheduler.AddMetadataCleanup(cleanup =>
{
    cleanup.AddTrainType<IHealthCheckTrain>();
    cleanup.AddTrainType<ISyncCustomersTrain>();
});
```

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core              pipelines, steps, railway error propagation
└→ Trax.Effect         + execution logging, DI, pluggable storage
   └→ Trax.Mediator       + decoupled dispatch via TrainBus
      └→ Trax.Scheduler   ← you are here
         └→ Trax.Api             + GraphQL API for remote access
            └→ Trax.Dashboard       + Blazor monitoring UI
```

**Next layer:** When you need a programmatic interface for external consumers — queuing jobs, running trains on demand, and querying state over HTTP — add [Trax.Api.GraphQL](https://www.nuget.org/packages/Trax.Api.GraphQL/).

Full documentation: [traxsharp.github.io/Trax.Docs](https://traxsharp.github.io/Trax.Docs)

## License

MIT
