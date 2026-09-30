# Trax.Scheduler

[![Build](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Scheduler)](https://www.nuget.org/packages/Trax.Scheduler)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Scheduler/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Scheduler)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Scheduler/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/scheduler)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Scheduler adds cron schedules, retries, dead letters and remote or Lambda workers for Trax trains. It builds on
[Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator), and Trax.Api and Trax.Dashboard read and manage what it
schedules.

## Install

```bash
dotnet add package Trax.Scheduler
dotnet add package Trax.Effect.Data.Postgres    # or Trax.Effect.Data.Sqlite for a single process
```

## Example

Adapted from the game server sample. Schedules, the work queue and dead letters live in the database, so the scheduler
needs a storage package:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(RecalculateLeaderboardTrain).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<IRecalculateLeaderboardTrain>(
            "leaderboard-na",
            new RecalculateLeaderboardInput { Region = "na" },
            Every.Minutes(5),
            options => options.MaxRetries(3))));
```

At startup this writes a manifest with the id `leaderboard-na` (updating it if it exists). Every five minutes the
scheduler queues a run of the train with that input, and a worker in the same process claims it and runs it. Each run
gets a row in `trax.metadata`, like any other. The input type implements `IManifestProperties` so it can be stored with
the manifest.

## Schedules

| Helper | Example |
|---|---|
| `Every.Seconds/Minutes/Hours/Days(n)` | `Every.Minutes(5)` |
| `Cron.Minutely/Hourly/Daily/Weekly/Monthly(...)` | `Cron.Weekly(DayOfWeek.Monday, hour: 6)` |
| `Cron.Expression("...")` | 5 fields, or 6 with seconds first: `Cron.Expression("*/15 * * * * *")` |
| `ScheduleOnce<TTrain>(id, input, delay)` | runs once after the delay, then disables the manifest |

Skip runs with exclusions: `options.Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday))`, and likewise
`Exclude.Dates`, `Exclude.DateRange` and `Exclude.TimeWindow` (which may cross midnight). `ITraxScheduler` creates,
triggers and cancels manifests at runtime.

## Retries and dead letters

A failed run is retried with backoff: 5 minutes, doubling each time, capped at an hour, all configurable on the builder
(`DefaultRetryDelay`, `RetryBackoffMultiplier`, `MaxRetryDelay`). Once a manifest's failures reach `MaxRetries` (default
3), it gets a dead letter and is not queued again until someone requeues or acknowledges it, from the dashboard, the
GraphQL API or `ITraxScheduler.RequeueDeadLetterAsync`.

## Dependent trains

`.Include<T>()` schedules a train that runs after the root manifest succeeds, and `.ThenInclude<T>()` one that runs after
the previous one. `IncludeMany` and `ThenIncludeMany` fan out. A dependent marked `options.Dormant()` runs only when a
junction of the parent activates it through `IDormantDependentContext.ActivateAsync`.

## Where trains run

The train class is the same in every case; only the host configuration changes.

| Where | Configure | Package |
|---|---|---|
| The scheduler host | Nothing: local worker threads claim queued jobs from the database | Trax.Scheduler |
| Worker processes against the same Postgres | `AddTraxWorker(o => o.WorkerCount = 4)` in each worker. Workers poll and claim jobs with `FOR UPDATE SKIP LOCKED`, one worker at a time; nothing calls them over HTTP | Trax.Scheduler |
| An HTTP runner | `UseRemoteWorkers(...)` for queued jobs and `UseRemoteRun(...)` for direct runs; the runner calls `AddTraxJobRunner` and `UseTraxJobRunner()` or `UseTraxRunEndpoint()` | Trax.Scheduler |
| Amazon SQS | `UseSqsWorkers(...)`, consumed by `SqsJobRunnerHandler` | Trax.Scheduler.Sqs |
| AWS Lambda | `UseLambdaWorkers(...)` and `UseLambdaRun(...)`; the function derives from `TraxLambdaFunction` | Trax.Scheduler.Lambda, Trax.Runner.Lambda |

The remote options route only the trains you name with `ForTrain<T>()` or mark `[TraxRemote]`; the rest keep running
locally. The scheduler's own work is done by five internal trains (ManifestManager, JobDispatcher, JobRunner,
MetadataCleanup and DeadLetterCleanup), and their runs are recorded too.

## Packages

| Package | What it adds |
|---|---|
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler) | Schedules, retries, dead letters, dependent trains, local and HTTP workers |
| [Trax.Scheduler.Sqs](https://www.nuget.org/packages/Trax.Scheduler.Sqs) | Dispatch jobs through Amazon SQS |
| [Trax.Scheduler.Lambda](https://www.nuget.org/packages/Trax.Scheduler.Lambda) | Dispatch jobs and runs to AWS Lambda |
| [Trax.Runner.Lambda](https://www.nuget.org/packages/Trax.Runner.Lambda) | Base class for a Lambda function that runs trains |

## What it does not do

- If a process dies halfway through a run, the run is marked failed and a scheduled train is retried from its first
  junction, so junctions that call other systems should be safe to repeat.
- Postgres is the production database: workers coordinate through its row and advisory locks. SQLite works for a single
  process and in-memory storage for tests. There is no SQL Server or MySQL provider.

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Scheduler.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| **[Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler)** | **Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda** |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [Scheduler overview](https://traxsharp.net/docs/scheduler)
- [Scheduling options](https://traxsharp.net/docs/scheduler/scheduling-options)
- [Exclusions](https://traxsharp.net/docs/scheduler/exclusions)
- [Dead letters and cleanup](https://traxsharp.net/docs/scheduler/dead-letters-and-cleanup)
- [Dependent trains](https://traxsharp.net/docs/scheduler/dependent-trains)
- [Remote execution](https://traxsharp.net/docs/scheduler/remote-execution)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Scheduler/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Scheduler/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
