# Trax.Samples

[![Build](https://github.com/TraxSharp/Trax.Samples/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Samples/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Samples.Templates)](https://www.nuget.org/packages/Trax.Samples.Templates)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Samples/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Samples)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Samples/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/samples)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

`Trax.Samples` holds complete Trax sample apps and the `dotnet new` templates for an API, a scheduler or a hub. It sits
last in the stack and uses every other layer; the templates ship as
[Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates), and `trax generate` in
[Trax.Cli](https://github.com/TraxSharp/Trax.Cli) builds on the hub template.

## Example

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
cd MyApp && dotnet run
```

Each template stores runs in memory, so it needs no database; swap `UseInMemory()` for `UsePostgres(...)` to keep them.

## Samples

Each sample is a working app with its own projects under `samples/`. ChatService is the most complete.

| Sample | What it shows | Storage |
|---|---|---|
| [ChatService](https://github.com/TraxSharp/Trax.Samples/blob/main/samples/ChatService/README.md) | A chat app: mutation trains, and a lifecycle hook that publishes each result to room-scoped GraphQL subscriptions over WebSockets, with a React client | SQLite |
| [GameServer](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/LocalWorkers) | A GraphQL API (API keys and JWT) that queues work, and a scheduler whose local workers run leaderboard, rewards and match trains, with the dashboard | Postgres |
| [Bookworm](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/Bookworm) | Two domains, each with its own schema and DbContext, joined by a cross-schema GraphQL edge; also adopts the architecture-guard fixtures | Postgres |
| [GraphQLClient](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/GraphQLClient) | A gateway calling two downstream Trax servers through keyed Trax GraphQL clients, all started by one `dotnet run` | In memory |
| [EnergyHub](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/DistributedWorkers) | A hub (GraphQL, scheduler, dashboard) that runs no trains itself, and a separate worker process that claims and runs them | Postgres, RabbitMQ |
| [ContentShield](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/EphemeralWorkers) | An API that sends queued and run mutations over HTTP to a runner process standing in for a Lambda function | Postgres, RabbitMQ |
| [PersistedOperations](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/PersistedOperations) | An API that accepts only persisted operations, and a client that uploads, queries and hot-fixes them | Postgres |
| [StateMachine](https://github.com/TraxSharp/Trax.Samples/blob/main/samples/StateMachine/README.md) | Two snapshot state machines (`turnstile`, `checkout`) served over GraphQL, with a React client driving them through the `stateMachine` mutations | Postgres |
| [SignalRDashboard](https://github.com/TraxSharp/Trax.Samples/blob/main/samples/SignalRDashboard/README.md) | A Blazor Server page showing live train lifecycle events from the SignalR broadcaster | Postgres |
| [ApiAudit](https://github.com/TraxSharp/Trax.Samples/blob/main/samples/ApiAudit/Trax.Samples.ApiAudit/README.md) | Every GraphQL request captured as an audit entry and written in batches to a console sink | SQLite |
| [JobHunt](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/JobHunt) | The baseline of a job-hunt CRM: GraphQL, API keys, the dashboard and domain migrations, with a React client | Postgres |
| [TestRunner](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/TestRunner) | NUnit test projects run as queued trains, with results streamed to a React client over subscriptions | Postgres |
| [DataPipeline](https://github.com/TraxSharp/Trax.Samples/tree/main/samples/DataPipeline) | The scheduler running three [Flowthru](https://github.com/chaoticgoodcomputing/flowthru) data pipelines as a dependency chain | Postgres |

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Samples.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| **[Trax.Samples](https://github.com/TraxSharp/Trax.Samples)** | **Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates** |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Samples/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Samples/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
