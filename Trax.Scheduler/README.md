# Trax.Scheduler

[![Build](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Scheduler/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Scheduler)](https://www.nuget.org/packages/Trax.Scheduler)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Scheduler/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Scheduler)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Scheduler/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/scheduler)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
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

Adapted from the game server sample:

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

Every five minutes the scheduler queues a run with that input and a worker claims it. A failed run retries with backoff,
then lands in the dead-letter queue.

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

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Scheduler/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Scheduler/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
