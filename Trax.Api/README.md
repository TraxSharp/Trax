# Trax.Api

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Api.GraphQL)](https://www.nuget.org/packages/Trax.Api.GraphQL/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

GraphQL API for [Trax](https://www.nuget.org/packages/Trax.Effect/) — expose train discovery, execution, and scheduler operations over HTTP via HotChocolate.

## What This Does

Adds a programmatic interface to your train network. External consumers can discover registered trains, run them on demand, queue work for the scheduler, and manage manifests — all through a typed GraphQL schema.

The API is designed to run on a **separate machine** from the scheduler. Both share a PostgreSQL database: the API writes work queue entries, the scheduler polls and dispatches. This means the API server is a thin HTTP layer — no polling services or background workers.

## Installation

```bash
dotnet add package Trax.Api.GraphQL
```

`Trax.Api.GraphQL` depends on `Trax.Api` — you don't need to reference it directly.

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraxEffects(options => options
    .AddServiceTrainBus(typeof(Program).Assembly)
    .AddPostgresEffect(connectionString)
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

## Packages

| Package | Description |
|---------|-------------|
| `Trax.Api` | Core library — DTOs, health check, shared service registration |
| `Trax.Api.GraphQL` | HotChocolate schema (queries, mutations, subscriptions) |

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core              pipelines, steps, railway error propagation
└→ Trax.Effect         + execution logging, DI, pluggable storage
   └→ Trax.Mediator       + decoupled dispatch via TrainBus
      └→ Trax.Scheduler      + cron schedules, retries, dead-letter queues
         └→ Trax.Api          ← you are here
            └→ Trax.Dashboard       + Blazor monitoring UI
```

**Next layer:** When you need a monitoring UI for inspecting trains, browsing execution history, and managing manifests from a browser, add [Trax.Dashboard](https://www.nuget.org/packages/Trax.Dashboard/).

Full documentation: [traxsharp.github.io/Trax.Docs](https://traxsharp.github.io/Trax.Docs)

## License

MIT
