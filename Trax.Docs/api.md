---
layout: default
title: API
nav_order: 7
---

# API

Trax.Api adds a programmatic interface to the scheduling and train execution system. It ships as two NuGet packages — a core library and a GraphQL transport powered by HotChocolate.

The API is designed to run on a **separate machine** from the scheduler. The two share a database: the API writes work queue entries and manifest updates, the scheduler polls them and dispatches trains. This means the API server doesn't run polling services or background workers — it's a thin HTTP layer over the shared state.

## Two Execution Modes

| Mode | How It Works | When to Use |
|------|-------------|-------------|
| **Queue** (delegated) | Creates a `WorkQueue` entry in the database. The scheduler picks it up on its next poll cycle and dispatches it on the scheduler machine. | Heavy trains, recurring work, anything that should run on dedicated scheduler infrastructure. |
| **Run** (direct) | Calls `ITrainBus.RunAsync` on the API machine. The train executes in-process, blocking until completion. | Lightweight on-demand trains where you need the result immediately. Requires the train's assemblies to be registered on the API machine. |

Trains opt into the GraphQL schema with the [`[TraxQuery]` or `[TraxMutation]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-graphql-attribute.md %}) attributes. Only annotated trains get typed fields generated.

## Quick Setup

```bash
dotnet add package Trax.Api.GraphQL
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJson()
    )
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxGraphQL();  // maps at /trax/graphql — opens Banana Cake Pop IDE in browser
app.MapHealthChecks("/trax/health");

app.Run();
```

*SDK Reference: [AddTraxGraphQL]({{ site.baseurl }}{% link sdk-reference/graphql-api/add-trax-graphql.md %})*

## Packages

| Package | Description |
|---------|-------------|
| `Trax.Api` | Core library — DTOs, health check, shared service registration |
| `Trax.Api.GraphQL` | HotChocolate schema (queries, mutations, subscriptions) |

`Trax.Api.GraphQL` depends on `Trax.Api` — you don't need to reference it directly.

## Architecture

```
┌─────────────────────────────┐    ┌─────────────────────────────┐
│         API Server          │    │      Scheduler Server       │
│                             │    │                             │
│  GraphQL endpoint           │    │  ManifestManagerPolling     │
│  ITrainBus (direct runs)    │    │  JobDispatcherPolling       │
│  ITraxScheduler (DB writes) │    │  JobRunner                  │
│  ITrainDiscoveryService     │    │                             │
│  ITrainExecutionService     │    │                             │
└──────────────┬──────────────┘    └──────────────┬──────────────┘
               │                                   │
               └──────────┬───────────────────────┘
                          │
                          ▼
               ┌──────────────────────┐
               │     PostgreSQL       │
               │                      │
               │  manifests           │
               │  work_queue          │
               │  metadata            │
               │  dead_letters        │
               └──────────────────────┘
```

The API server doesn't need `AddScheduler()` — it only needs `AddMediator()` (for train discovery and direct execution) and a data provider (for DB access). The scheduler configuration (`AddScheduler`) runs on the scheduler machine only.

However, if you want the API to also schedule manifests at startup (like the scheduler does), you can add `AddScheduler()` on the API machine as well. The polling services can be disabled with configuration if you only want startup seeding.

## Health Check

The API includes an ASP.NET Core `IHealthCheck` that queries the database and reports:

- **Queue depth** — work items waiting for dispatch
- **In-progress** — currently executing trains
- **Failed (last hour)** — recent failures
- **Dead letters** — unresolved dead letter entries

Returns `Healthy` when everything looks normal, `Degraded` when dead letters exist or recent failures exceed a threshold. The same data is available as a [GraphQL query]({{ site.baseurl }}{% link sdk-reference/graphql-api/queries.md %}#health) for consumers that prefer structured access over the standard health endpoint.

```csharp
builder.Services.AddHealthChecks().AddTraxHealthCheck();
app.MapHealthChecks("/trax/health");
```

## Authentication & Middleware

Trax doesn't include built-in auth — you add it with standard ASP.NET Core middleware. `UseTraxGraphQL` accepts a `configure` callback for applying endpoint conventions like authorization, rate limiting, or CORS:

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

The callback receives an `IEndpointConventionBuilder` and supports `.RequireAuthorization()`, `.RequireRateLimiting()`, `.RequireCors()`, `.AddEndpointFilter<T>()`, and any other endpoint convention.

For global auth that applies to everything (including non-Trax routes), use middleware instead:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

### Per-Train Authorization

Endpoint-level auth answers "can this user access the API?" For finer control, decorate individual train classes with `[TraxAuthorize]`:

```csharp
[TraxAuthorize("Admin")]
[TraxMutation(Operations = GraphQLOperation.Run)]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

When the API receives a request to run or queue this train, it checks the current user against the policy before executing. Trains without `[TraxAuthorize]` have no per-train restriction. See the [Authorization]({{ site.baseurl }}{% link authorization.md %}) guide for details.

## Named GraphQL Schema

The GraphQL API registers on a **named HotChocolate schema** (`"trax"`) rather than the default unnamed schema. This means it won't conflict with your own `AddGraphQLServer()` calls — both can coexist in the same application at different paths.

## Sample Projects

The API is demonstrated in two samples, each using a different deployment topology:

- **LocalWorkers (GameServer)** — API and scheduler as separate processes. The GraphQL API handles lightweight trains directly and queues heavy work for the scheduler. See `samples/LocalWorkers/Trax.Samples.GameServer.Api`.
- **DistributedWorkers (EnergyHub)** — API, scheduler, and dashboard in a single hub process. The hub schedules and serves GraphQL but offloads execution to separate worker processes. See `samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub`.

Both follow the [trains library pattern]({{ site.baseurl }}{% link samples.md %}) — trains live in a shared library, executables are thin wrappers that pick which capabilities to enable.

## SDK Reference

For complete endpoint documentation, request/response schemas, and method signatures:

- [GraphQL API]({{ site.baseurl }}{% link sdk-reference/graphql-api.md %}) — queries, mutations, subscriptions, HotChocolate setup
- [Authorization]({{ site.baseurl }}{% link authorization.md %}) — per-train authorization with `[TraxAuthorize]`
- [TrainDiscovery]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-discovery.md %}) — how train discovery works
- [TrainExecution]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-execution.md %}) — queue and run services

## Next Layer

When you need a monitoring UI for inspecting trains, browsing execution history, and managing manifests from a browser, add [Trax.Dashboard](dashboard.md).
