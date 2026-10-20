# Trax .NET

[![CI](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml/badge.svg)](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages?q=Trax)
[![npm](https://img.shields.io/npm/v/@traxsharp/state-machine)](https://www.npmjs.com/package/@traxsharp/state-machine)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

A headless web framework for .NET. Build a more robust frontend: whatever goes wrong, the backend gets it right, and
every run is recorded in your Postgres.
[What Trax does](https://traxsharp.net/docs/what-trax-does) · [Docs](https://traxsharp.net/docs) ·
[Getting started](https://traxsharp.net/docs/getting-started) ·
[Samples](Trax.Samples) · [NuGet](https://www.nuget.org/profiles/Theauxm) ·
[npm](https://www.npmjs.com/package/@traxsharp/state-machine)

## Install

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
```

Every Trax package releases at one version, so install the same version of each one you take.

## Example

A train is a chain of junctions. Run it directly, queue it, schedule it, or let Trax.Api serve it as GraphQL; each
run writes a row to `trax.metadata`. A state machine marked `[TraxMachine]` is served as its own typed API, applied on
the server, and a machine state can run a train.

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
| [Trax.Api](Trax.Api) | GraphQL generated from your trains and machines: a mutation per machine event, authorization checked on the server, persisted operations, audit, live subscriptions and the operations console your host serves |
| [Trax.Cli](Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |
| [Trax.Api.StateMachine](Trax.Api.StateMachine) | `@traxsharp/state-machine`, the TypeScript twin of the state-machine engine |
| [Trax.Docs](Trax.Docs) | The documentation published at [traxsharp.net/docs](https://traxsharp.net/docs), and the decision records |
| [Trax.Website](Trax.Website) | The source of [traxsharp.net](https://traxsharp.net) |

Trax used to be split into one repository per layer (`TraxSharp/Trax.Core` and the rest). Those are archived; their
history is here, under each folder.

### The coloured squares

Release notes, pull requests and the docs mark each change with the parts of Trax it touches. The parts are concepts,
not folders: `Trax.Effect` alone holds data, plumbing, trains and state machines. A change that crosses several shows
several squares, innermost first.

| | Part | What it covers |
|---|---|---|
| 🟨 | Data | The run-record tables, migrations and the Postgres, SQLite and in-memory providers |
| 🟫 | Plumbing | Effect and junction providers, broadcasters (RabbitMQ, SignalR), lifecycle hooks |
| 🟩 | Trains | Trains, junctions, chains, decisions, `Parallel` |
| 🟪 | Machines | State machines, in .NET and in `@traxsharp/state-machine` |
| 🟧 | Dispatch | The train bus, authorization, the scheduler (manifests, retries, dead letters), remote workers and Lambda |
| 🟦 | Surface | Trax.Api: GraphQL, auth, persisted operations, generated clients, the operations console |
| ⬛ | Tooling | The `trax` CLI, analyzers, architecture guards, CI, releases, the docs |
| 🟥 | Security | Not a part: a flag on any change to authentication, authorization or what is exposed |

A release note headed 🟧🟦🟥, for example, changes how work is dispatched and what the API serves, and it is worth
reading for its security impact before you upgrade.

## Contributing

Read [AGENTS.md](AGENTS.md) before changing anything, then the `AGENTS.md` in the folder you are working in. Report
vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
