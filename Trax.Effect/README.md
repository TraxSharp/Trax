# Trax.Effect

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages/Trax.Effect)
[![codecov](https://codecov.io/gh/TraxSharp/Trax/branch/main/graph/badge.svg?flag=Trax.Effect)](https://app.codecov.io/gh/TraxSharp/Trax?flags%5B0%5D=Trax.Effect)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Effect/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/effect)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.Effect adds run records, dependency injection and storage providers to Trax trains, plus the portable state-machine engine. Its `ServiceTrain` runs the same chain as a [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) `Train`, resolves junctions from DI, and writes a metadata row for every run. Trax.Mediator, Trax.Scheduler and the layers above it all run trains through it.

## Install

```bash
dotnet add package Trax.Effect
dotnet add package Trax.Effect.Data.Postgres   # or Trax.Effect.Data.Sqlite, or Trax.Effect.Data.InMemory
```

Install one storage package, since runs are recorded only through one: Postgres for production, SQLite for a single process, InMemory for tests.

## Example

Adapted from the game server sample:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()
        .AddJunctionLogger()));

builder.Services.AddScopedTraxRoute<IRecalculateLeaderboardTrain, RecalculateLeaderboardTrain>();

public interface IRecalculateLeaderboardTrain
    : IServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>;

public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain
{
    protected override Task<Either<Exception, RecalculateLeaderboardOutput>> Junctions() =>
        Chain<AggregateScoresJunction>()
            .Chain<RankPlayersJunction>()
            .Resolve();
}

public class RankPlayersJunction(ILogger<RankPlayersJunction> logger)
    : Junction<RecalculateLeaderboardInput, RecalculateLeaderboardOutput> { /* ... */ }
```

Inject `IRecalculateLeaderboardTrain` and call `Run(input)`. Each run writes a row to `trax.metadata`: when it started,
how it ended, and on failure the junction that threw and its exception. `SaveTrainParameters()` adds the input and output.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Effect.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| **[Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect)** | **A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine** |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax/tree/main/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Effect/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Effect/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
