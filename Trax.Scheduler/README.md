# Trax.Scheduler

[![Build](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler)](https://www.nuget.org/packages/Trax.Scheduler/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Scheduler)](https://www.nuget.org/packages/Trax.Scheduler/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Scheduler/blob/main/LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Scheduler)](https://github.com/TraxSharp/Trax.Scheduler/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Scheduler/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Scheduler)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

Timetable management for [Trax](https://www.nuget.org/packages/Trax.Effect/) trains — recurring schedules, automatic retries, dead-letter handling, and dependent departures.

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Scheduler.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| **[Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler)** | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

## What This Does

If you have trains that need to run on a timetable — ETL pipelines, data syncs, nightly reports, periodic cleanup — Trax.Scheduler handles the dispatch. You write a manifest for each train (what cargo it carries, when it departs, how many times to retry if it derails), and the scheduler takes care of the rest.

Every scheduled run is a normal train journey, so you get the same journey logging, station services, and control room visibility as any other train.

## Installation

```bash
dotnet add package Trax.Scheduler
dotnet add package Trax.Effect.Data.Postgres
```

**The scheduler needs a persistent data provider.** Manifests, the work queue and dead letters live in the database, so `Trax.Scheduler` alone is not enough: add `Trax.Effect.Data.Postgres` and call `UsePostgres(...)` (or `Trax.Effect.Data.Sqlite` and `UseSqlite(...)` for a single-node host). `AddScheduler()` throws at startup when no data provider is configured. `Trax.Effect.Data.InMemory` (`UseInMemory()`) also satisfies that check, but nothing survives a restart, so keep it to tests.

Optional packages, for running trains outside the scheduler host:

| Package | Use it when |
|---------|-------------|
| `Trax.Scheduler.Lambda` | the scheduler invokes an AWS Lambda function directly (`UseLambdaWorkers`, `UseLambdaRun`) |
| `Trax.Runner.Lambda` | you are writing that Lambda function (`TraxLambdaFunction` base class) |
| `Trax.Scheduler.Sqs` | jobs go to workers through an Amazon SQS queue (`UseSqsWorkers`, `SqsJobRunnerHandler`) |
| `Trax.Scheduler.Tests.ArrayLogger` | a test needs to assert on what was logged (in-memory `ILoggerProvider`) |

## Setup

A scheduled train is an ordinary Trax train whose input implements `IManifestProperties`, so the scheduler can store it with the manifest:

```csharp
using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TraxDatabase")!;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IGenerateReportTrain>(
                "nightly-report",
                new GenerateReportInput { Format = "pdf" },
                Cron.Daily(hour: 3)
            )
        )
);

builder.Build().Run();

public record GenerateReportInput : IManifestProperties
{
    public string Format { get; init; } = "pdf";
}

public interface IGenerateReportTrain : IServiceTrain<GenerateReportInput, Unit>;

public class GenerateReportTrain : ServiceTrain<GenerateReportInput, Unit>, IGenerateReportTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<RenderReportJunction>().Resolve();
}

public class RenderReportJunction : Junction<GenerateReportInput, Unit>
{
    public override Task<Unit> Run(GenerateReportInput input) => Task.FromResult(Unit.Default);
}
```

`Schedule<TTrain>` infers the input type from the train's `IServiceTrain<TInput, TOutput>` interface. The explicit form `Schedule<IGenerateReportTrain, GenerateReportInput, Unit>(...)` takes all three type arguments; there is no two-argument overload. Manifests declared here are upserted by external ID when the host starts.

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
public class CheckDataJunction(IDormantDependentContext dormants) : Junction<ExtractInput, Unit>
{
    public override async Task<Unit> Run(ExtractInput input)
    {
        if (input.AnomaliesDetected)
        {
            await dormants.ActivateAsync<IQualityCheckTrain, QualityCheckInput, Unit>(
                "quality-0",
                new QualityCheckInput { /* ... */ }
            );
        }

        return Unit.Default;
    }
}
```

## Scheduling at Runtime

Inject `ITraxScheduler` to create, trigger or cancel manifests after startup. Its methods take the train, input and output types explicitly:

```csharp
public class ReportController(ITraxScheduler scheduler)
{
    public Task<Manifest> ScheduleWeekly(string format) =>
        scheduler.ScheduleAsync<IGenerateReportTrain, GenerateReportInput, Unit>(
            $"weekly-report-{format}",
            new GenerateReportInput { Format = format },
            Cron.Weekly(DayOfWeek.Monday, hour: 6)
        );

    public Task<Manifest> RunInFiveMinutes() =>
        scheduler.ScheduleOnceAsync<IGenerateReportTrain, GenerateReportInput, Unit>(
            new GenerateReportInput(),
            TimeSpan.FromMinutes(5)
        );

    public Task<int> Cancel(string externalId) => scheduler.CancelAsync(externalId);
}
```

`ITraxScheduler` lives in `Trax.Scheduler.Services.TraxScheduler`, `Manifest` in `Trax.Effect.Models.Manifest`.

## How the Yard Works

The scheduler runs as three internal trains — all visible in the control room with full journey logging:

1. **ManifestManager** — the yard master. Polls on an interval, checks which manifests are due, and queues departures.
2. **JobDispatcher** — the dispatcher. Reads the departure queue, respects per-line capacity limits, and assigns trains to the track.
3. **JobRunner** — the engineer. Picks up an assigned job and drives the train through its route, recording the outcome.

## Journey Log Cleanup

Long-running timetables accumulate journey records. Configure automatic deletion of finished runs per train type:

```csharp
scheduler.AddMetadataCleanup(cleanup =>
{
    cleanup.RetentionPeriod = TimeSpan.FromHours(2);
    cleanup.AddTrainType<IHealthCheckTrain>();
    cleanup.AddTrainType<ISyncCustomersTrain>(TimeSpan.FromDays(7));
});
```

## Next Layer

When you need a programmatic interface for external consumers (queuing jobs, running trains on demand, querying state over HTTP), move up to [Trax.Api](https://github.com/TraxSharp/Trax.Api).

## Documentation

Guides, the full scheduler reference and the architecture of the whole stack: [traxsharp.net/docs](https://traxsharp.net/docs).

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
