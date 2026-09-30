# Trax.Effect

[![Build](https://github.com/TraxSharp/Trax.Effect/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Effect/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages/Trax.Effect)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Effect/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Effect)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Effect/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/effect)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Effect adds run records, dependency injection and storage providers to Trax trains, plus the portable state-machine engine. Its `ServiceTrain` runs the same chain as a [Trax.Core](https://github.com/TraxSharp/Trax.Core) `Train`, resolves junctions from DI, and writes a metadata row for every run. Trax.Mediator, Trax.Scheduler and the layers above it all run trains through it.

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

Inject `IRecalculateLeaderboardTrain` and call `Run(input)`. The junctions' constructor arguments come from the container. Before the first junction runs, a row is written to `trax.metadata` in state `InProgress`. When the train ends it becomes `Completed` or `Failed`, with the end time, and on failure the junction that failed and the exception. `SaveTrainParameters()` stores the input and output as JSON on the same row. Trax.Mediator's `AddMediator` registers every train in an assembly, so most apps do not call `AddScopedTraxRoute` themselves (`AddTransientTraxRoute` and `AddSingletonTraxRoute` also exist).

## Metadata

Each run's row moves through `Pending`, `InProgress`, then `Completed`, `Failed` or `Cancelled`. It carries the train name, start and end times, the host name, and on failure `FailureJunction`, `FailureException` and `FailureReason`. The dashboard shows the same rows, and so does a SQL query.

If a process dies halfway through a run, the run is marked failed and a scheduled train is retried from its first junction, so junctions that call other systems should be safe to repeat.

## Packages

Each provider adds one method to the `AddEffects` builder, except the broadcasters, which add theirs inside `UseBroadcaster(b => ...)`.

| Package | Method | What it adds |
|---|---|---|
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect) | `AddTrax`, `AddEffects` | `ServiceTrain`, run records, lifecycle hooks, DI registration |
| [Trax.Effect.Data.Postgres](https://www.nuget.org/packages/Trax.Effect.Data.Postgres) | `UsePostgres(connectionString)` | PostgreSQL storage, applying the Trax migrations at startup. For production |
| [Trax.Effect.Data.Sqlite](https://www.nuget.org/packages/Trax.Effect.Data.Sqlite) | `UseSqlite(connectionString)` | SQLite storage for a single process |
| [Trax.Effect.Data.InMemory](https://www.nuget.org/packages/Trax.Effect.Data.InMemory) | `UseInMemory()` | In-memory storage for tests, lost on exit |
| [Trax.Effect.Data](https://www.nuget.org/packages/Trax.Effect.Data) | | The data layer the storage packages share. Installed with them |
| [Trax.Effect.Provider.Parameter](https://www.nuget.org/packages/Trax.Effect.Provider.Parameter) | `SaveTrainParameters()` | Effect provider: stores each run's input and output, masking `[TraxSensitive]` members |
| [Trax.Effect.Provider.Json](https://www.nuget.org/packages/Trax.Effect.Provider.Json) | `AddJson()` | Effect provider: logs tracked model state as JSON, for debugging |
| [Trax.Effect.JunctionProvider.Logging](https://www.nuget.org/packages/Trax.Effect.JunctionProvider.Logging) | `AddJunctionLogger()` | Junction provider: logs each junction's start, finish and duration, optionally its output |
| [Trax.Effect.JunctionProvider.Progress](https://www.nuget.org/packages/Trax.Effect.JunctionProvider.Progress) | `AddJunctionProgress()` | Junction provider: records the running junction and checks for cancellation between junctions. Needs a storage package |
| [Trax.Effect.Broadcaster.RabbitMQ](https://www.nuget.org/packages/Trax.Effect.Broadcaster.RabbitMQ) | `UseRabbitMq(connectionString)` | Train lifecycle events delivered to other processes over RabbitMQ |
| [Trax.Effect.Broadcaster.SignalR](https://www.nuget.org/packages/Trax.Effect.Broadcaster.SignalR) | `UseSignalRHub()`, `MapTraxTrainEventHub()` | Train lifecycle events pushed to browser and Blazor clients |
| [Trax.Effect.StateMachine](https://www.nuget.org/packages/Trax.Effect.StateMachine) | | The snapshot state-machine engine, with no dependencies |
| [Trax.Effect.StateMachine.Persistence](https://www.nuget.org/packages/Trax.Effect.StateMachine.Persistence) | `AddStateMachines(assemblies)` | Stores state-machine snapshots and drafts through your storage package, with run-once effects |
| [Trax.Effect.StateMachine.Testing](https://www.nuget.org/packages/Trax.Effect.StateMachine.Testing) | | Checks that the C# and TypeScript engines agree on a generated corpus |
| [Trax.Effect.Data.Testing](https://www.nuget.org/packages/Trax.Effect.Data.Testing) | | Architecture guards for your data layer |

The state-machine packages hold a multi-step flow (a wizard, a checkout) as a JSON snapshot that a C# backend and a TypeScript client both read. See [State machines](https://traxsharp.net/docs/statemachine).

## What it does not do

- Storage is PostgreSQL, SQLite or in memory. There is no SQL Server or MySQL provider.
- A failed run is not resumed at the junction that failed.
- The broadcasters send lifecycle events. They do not carry messages between services.
- Runs are recorded in the database and logged through `ILogger`, not emitted as OpenTelemetry spans.

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Effect.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| **[Trax.Effect](https://github.com/TraxSharp/Trax.Effect)** | **A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine** |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [Effect overview](https://traxsharp.net/docs/effect)
- [Metadata](https://traxsharp.net/docs/effect/metadata)
- [Effect providers](https://traxsharp.net/docs/effect/effect-providers)
- [Configuration reference](https://traxsharp.net/docs/sdk-reference/configuration)
- [State machines](https://traxsharp.net/docs/statemachine)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Effect/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Effect/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
