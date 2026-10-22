# Trax.Api.Console.Terminal

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Api.Console.Terminal)](https://www.nuget.org/packages/Trax.Api.Console.Terminal)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/reference/trax-console)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

`trax-console` is the Trax operations console in a terminal: runs, dead letters, the work queue, manifests, a run's
trace and machine instances, read and acted on over SSH or anywhere without a browser. It sends only the documents the
browser console sends, so a host that serves the console has nothing new to allow, and the operations gate decides
every request.

## Install

```bash
dotnet tool install --global Trax.Api.Console.Terminal   # needs the .NET 10 runtime
```

Install the version of the Trax release the host runs: `trax-console check` says which.

## Example

```bash
export TRAX_CONSOLE_API_KEY=$(pass show trax/prod-operator)
trax-console --host https://ops.example.com                       # the console
trax-console --host https://ops.example.com list runs --where state=FAILED
trax-console --host https://ops.example.com do dead-letters requeue 7 9
```

With no command it opens the console; with one it answers in JSON lines, for scripts and agents. The credential
comes from `TRAX_CONSOLE_API_KEY`, `TRAX_CONSOLE_TOKEN`, `--api-key-file` or `--token-file`, never the command line.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Api.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers |
| [Trax.StateMachine](https://github.com/TraxSharp/Trax/tree/main/Trax.StateMachine) | State machines a page drives and the server applies, in C# and TypeScript, held to one conformance kit |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| **[Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api)** | **GraphQL generated from your trains, with authentication, audit and typed clients, and the operations console your host serves, in a browser or a terminal** |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.
