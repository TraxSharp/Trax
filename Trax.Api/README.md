# Trax.Api

[![Build](https://github.com/TraxSharp/Trax.Api/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Api/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Api.GraphQL)](https://www.nuget.org/packages/Trax.Api.GraphQL/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Api.GraphQL)](https://www.nuget.org/packages/Trax.Api.GraphQL/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Api)](https://github.com/TraxSharp/Trax.Api/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Api/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Api)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

GraphQL API for [Trax](https://www.nuget.org/packages/Trax.Effect/). Exposes train discovery, execution, and scheduler operations over HTTP via HotChocolate.

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Api.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| **[Trax.Api](https://github.com/TraxSharp/Trax.Api)** | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

## What This Does

Adds a programmatic interface to your train network. External consumers can discover registered trains, run them on demand, queue work for the scheduler, and manage manifests, all through a typed GraphQL schema.

The API is designed to run on a **separate machine** from the scheduler. Both share a PostgreSQL database: the API writes work queue entries, the scheduler polls and dispatches. This means the API server is a thin HTTP layer with no polling services or background workers.

## Installation

```bash
dotnet add package Trax.Api.GraphQL
```

`Trax.Api.GraphQL` depends on `Trax.Api`, so you don't need to reference it directly.

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL();  // maps at /trax/graphql

app.Run();
```

## Two Execution Modes

| Mode | How It Works | When to Use |
|------|-------------|-------------|
| **Queue** | Creates a `WorkQueue` entry. The scheduler picks it up and dispatches on the scheduler machine. | Heavy trains, recurring work, dedicated scheduler infrastructure. |
| **Run** | Calls `ITrainBus.RunAsync` in-process on the API machine. | Lightweight on-demand trains where you need the result immediately. |

Trains opt into the GraphQL schema with `[TraxQuery]` or `[TraxMutation]` attributes. Only annotated trains get typed fields generated.

## Authentication

`UseTraxGraphQL` accepts a `configure` callback for endpoint-level auth:

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

For per-train authorization, decorate train classes with `[TraxAuthorize]`:

```csharp
[TraxAuthorize("Admin")]
[TraxMutation(GraphQLOperation.Run)]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

## Security Disclaimer

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [SECURITY-DISCLAIMER.md](SECURITY-DISCLAIMER.md).

Trax.Api ships authentication (`Trax.Api.Auth`, `Trax.Api.Auth.ApiKey`) and audit (`Trax.Api.GraphQL.Audit`) packages. They provide the glue between ASP.NET Core's auth primitives and Trax's train dispatch. They do not guarantee that a system using them is secure. Read [SECURITY-DISCLAIMER.md](SECURITY-DISCLAIMER.md) before deploying.

## Packages

| Package | Description |
|---------|-------------|
| `Trax.Api` | Core library: DTOs, health check, shared service registration |
| `Trax.Api.GraphQL` | HotChocolate schema (queries, mutations, subscriptions) |
| `Trax.Api.Auth` | Principal abstraction and claim-type constants (no scheme). NO WARRANTY. |
| `Trax.Api.Auth.ApiKey` | API-key authentication handler. NO WARRANTY. |
| `Trax.Api.GraphQL.Audit` | GraphQL request audit pipeline (listener, channel, writer, sink). NO WARRANTY. |

## Next Layer

When you need a monitoring UI for inspecting trains, browsing execution history, and managing manifests from a browser, move up to [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard).

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
