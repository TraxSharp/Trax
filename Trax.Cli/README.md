# Trax.Cli

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Cli)](https://www.nuget.org/packages/Trax.Cli)
[![codecov](https://codecov.io/gh/TraxSharp/Trax/branch/main/graph/badge.svg?flag=Trax.Cli)](https://app.codecov.io/gh/TraxSharp/Trax?flags%5B0%5D=Trax.Cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Cli/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/reference/cli)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

`Trax.Cli` is the `trax` .NET tool. `trax generate` scaffolds a hub and a library of Trax trains from an OpenAPI or
GraphQL schema, and `trax machine` generates state-machine code. The hub comes from the `trax-hub` template in
[Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples); the trains are typed pipelines of small steps (junctions)
whose bodies you fill in.

## Install

```bash
dotnet new install Trax.Samples.Templates   # trax generate builds on the trax-hub template
dotnet tool install --global Trax.Cli       # needs the .NET 10 SDK
```

## Example

```bash
trax generate --schema ./schema.graphql --output ./Payments --name Payments
cd Payments/Payments.Hub && dotnet run

trax machine new checkout --output ./Machines --namespace MyApp.Machines
```

`generate` reads GraphQL SDL or an OpenAPI 3.0 document and writes a hub plus one train per operation, each junction
throwing `NotImplementedException` under a `TODO` for you to fill in. `trax <command> --help` lists every option.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Cli.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax/tree/main/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| **[Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli)** | **The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen** |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Cli/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Cli/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
