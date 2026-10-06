# Trax .NET

[![CI](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml/badge.svg)](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages?q=Trax)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

Business logic you can call, schedule, or serve as an API, with every run recorded in your Postgres.
[Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) ·
[Samples](Trax.Samples) · [NuGet](https://www.nuget.org/profiles/Theauxm)

## Install

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
```

Every Trax package releases at one version, so install the same version of each one you take.

## Example

A train is a chain of junctions. Run it directly, queue it, schedule it, or let Trax.Api serve it as GraphQL; each
run writes a row to `trax.metadata`.

```csharp
public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain
{
    protected override Task<Either<Exception, RecalculateLeaderboardOutput>> Junctions() =>
        Chain<AggregateScoresJunction>()
            .Chain<RankPlayersJunction>()
            .Resolve();
}
```

## Where this fits

Trax is split into layers, one folder each, and every package releases at one version from this repository. Each .NET
folder depends only on folders above it in this table; take the layers you need, and the trains you wrote do not change.

| Folder | What it adds |
|---|---|
| [Trax.Core](Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](Trax.Samples) | **Start here.** Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |
| [Trax.Api.StateMachine](Trax.Api.StateMachine) | `@trax/state-machine`, the TypeScript twin of the state-machine engine |
| [Trax.Docs](Trax.Docs) | The documentation published at [traxsharp.net/docs](https://traxsharp.net/docs), and the decision records |
| [Trax.Website](Trax.Website) | The source of [traxsharp.net](https://traxsharp.net) |

Trax used to be split into one repository per layer (`TraxSharp/Trax.Core` and the rest). Those are archived; their
history is here, under each folder.

## Contributing

Read [AGENTS.md](AGENTS.md) before changing anything, then the `AGENTS.md` in the folder you are working in. Report
vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
