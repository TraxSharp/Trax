# Trax.Mediator

[![Build](https://github.com/TraxSharp/Trax.Mediator/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Mediator/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Mediator)](https://www.nuget.org/packages/Trax.Mediator)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Mediator/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Mediator)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Mediator/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/mediator)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Mediator is the train bus for Trax: run a train by its input type, with every chain checked at host startup. It
builds on [Trax.Effect](https://github.com/TraxSharp/Trax.Effect), and Trax.Scheduler and Trax.Api run their trains
through it.

## Install

```bash
dotnet add package Trax.Mediator
dotnet add package Trax.Effect.Data.Postgres    # or Trax.Effect.Data.Sqlite, Trax.Effect.Data.InMemory
dotnet add package Trax.Mediator.Testing        # optional, architecture guards for your test project
```

Trax.Effect and Trax.Core come in transitively. The storage package is where each run's record is written.

## Example

Adapted from the game server sample. Register the assembly that holds your trains, then hand an input to `ITrainBus`:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(RecalculateLeaderboardTrain).Assembly));

public class LeaderboardController(ITrainBus trains) : ControllerBase
{
    [HttpPost("leaderboard/{region}/recalculate")]
    public Task<RecalculateLeaderboardOutput> Recalculate(string region) =>
        trains.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = region });
}
```

The controller never names `RecalculateLeaderboardTrain`. The bus looks up the train registered for
`RecalculateLeaderboardInput`, resolves it through its `IRecalculateLeaderboardTrain` interface, runs it in the current
request and returns its output. The run gets a row in `trax.metadata` like any other. `ITrainBus` is scoped, so inject it
into a controller or resolve it from a scope.

## Dispatch by input type

`AddMediator` scans the assemblies you pass for every `IServiceTrain<TIn, TOut>` and maps each input type to its train.
Each train needs its own interface (`IRecalculateLeaderboardTrain`), which is the name it is registered and recorded
under. When two trains take the same input type, `RunAsync` reaches only the first one registered. Run the other with
`RunByNameAsync`, which takes the interface's full name and runs exactly that train.

A junction can call `ITrainBus` to run another train. The inner run gets its own record; the bus does not link it to the
outer run.

## Checked at startup

Before any hosted service starts, the host reads every registered train's chain and refuses to start if one cannot run:
a junction whose input no earlier junction (or the train's input) provides, or a junction Trax cannot construct. Every
train is checked first, so one failed start lists all of them. `AddMediator(m => m.SkipChainVerification())` turns the
check off.

The host also refuses to start when a train carries `[TraxAuthorize]` and no `ITrainAuthorizationService` is registered.

## Authorization and concurrency limits

The bus is an in-process call and checks no authorization. `ITrainExecutionService` is the path for outside callers,
and the one Trax.Api uses: it runs or queues a train by name from JSON input, and checks the caller against the train's
`[TraxAuthorize]` before reading the input. Its runs are also where concurrency limits apply:

| Limit | Set with |
|---|---|
| All trains together | `GlobalConcurrentRunLimit(n)` |
| One train | `ConcurrentRunLimit<TTrain>(n)`, or `[TraxConcurrencyLimit(n)]` on the train |
| One authenticated caller | `PerPrincipalMaxConcurrentRun(n)` |

A run that hits a limit waits for a slot. Queued work is not gated here.

## Packages

| Package | What it adds |
|---|---|
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator) | The train bus, train discovery and registry, the startup chain check, `[TraxAuthorize]` enforcement and concurrency limits |
| [Trax.Mediator.Testing](https://www.nuget.org/packages/Trax.Mediator.Testing) | `TrainGuards.EveryTrainHasInterface`, an architecture guard that lists every train missing its interface |

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Mediator.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| **[Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator)** | **The train bus: run a train by handing over its input, with every chain checked at startup** |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [Mediator overview](https://traxsharp.net/docs/mediator)
- [Train discovery](https://traxsharp.net/docs/mediator/train-discovery)
- [ITrainBus reference](https://traxsharp.net/docs/sdk-reference/mediator-api/train-bus)
- [Train execution service](https://traxsharp.net/docs/sdk-reference/mediator-api/train-execution)
- [Concurrency limiting](https://traxsharp.net/docs/sdk-reference/mediator-api/concurrency-limiting)
- [Registration order](https://traxsharp.net/docs/reference/registration-order)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Mediator/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Mediator/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
