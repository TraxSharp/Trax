---
layout: default
title: API
nav_order: 7
---

# API

Trax.Api adds a programmatic interface to the scheduling and train execution system. It ships as three NuGet packages — a core library, a REST transport, and a GraphQL transport — so you can pick one or both without pulling in dependencies you don't need.

The API is designed to run on a **separate machine** from the scheduler. The two share a database: the API writes work queue entries and manifest updates, the scheduler polls them and dispatches trains. This means the API server doesn't run polling services or background workers — it's a thin HTTP layer over the shared state.

## Two Execution Modes

| Mode | How It Works | When to Use |
|------|-------------|-------------|
| **Queue** (delegated) | Creates a `WorkQueue` entry in the database. The scheduler picks it up on its next poll cycle and dispatches it on the scheduler machine. | Heavy trains, recurring work, anything that should run on dedicated scheduler infrastructure. |
| **Run** (direct) | Calls `ITrainBus.RunAsync` on the API machine. The train executes in-process, blocking until completion. | Lightweight on-demand trains where you need the result immediately. Requires the train's assemblies to be registered on the API machine. |

Both modes accept JSON input — each train doesn't get its own endpoint. You pass the train name and a JSON object, and the API resolves the correct input type via `ITrainDiscoveryService`.

## Quick Setup

### REST

```bash
dotnet add package Trax.Api.Rest
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(Program).Assembly])
        .AddPostgresEffect(connectionString)
        .AddJsonEffect()
);

builder.Services.AddTraxRestApi();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxRestApi();              // maps at /trax/api by default
app.MapHealthChecks("/trax/health");

app.Run();
```

*SDK Reference: [AddTraxRestApi]({{ site.baseurl }}{% link sdk-reference/rest-api/add-trax-rest-api.md %})*

### GraphQL

```bash
dotnet add package Trax.Api.GraphQL
```

```csharp
builder.Services.AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL();  // maps at /trax/graphql — opens Banana Cake Pop IDE in browser
```

*SDK Reference: [AddTraxGraphQL]({{ site.baseurl }}{% link sdk-reference/graphql-api/add-trax-graphql.md %})*

### Using Both

```csharp
builder.Services.AddTraxRestApi();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxRestApi();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");
```

## Packages

| Package | Description |
|---------|-------------|
| `Trax.Api` | Core library — DTOs, health check, shared service registration |
| `Trax.Api.Rest` | ASP.NET Core minimal API endpoint mappings |
| `Trax.Api.GraphQL` | HotChocolate schema (queries + mutations) |

`Trax.Api.Rest` and `Trax.Api.GraphQL` both depend on `Trax.Api` — you don't need to reference it directly.

## Architecture

```
┌─────────────────────────────┐    ┌─────────────────────────────┐
│         API Server          │    │      Scheduler Server       │
│                             │    │                             │
│  REST / GraphQL endpoints   │    │  ManifestManagerPolling     │
│  ITrainBus (direct runs)    │    │  JobDispatcherPolling       │
│  ITraxScheduler (DB writes) │    │  TaskServerExecutor         │
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

The API server doesn't need `AddScheduler()` — it only needs `AddServiceTrainBus()` (for train discovery and direct execution) and a data provider (for DB access). The scheduler configuration (`AddScheduler`) runs on the scheduler machine only.

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

Trax doesn't include built-in auth — you add it with standard ASP.NET Core middleware. Both `UseTraxRestApi` and `UseTraxGraphQL` accept a `configure` callback for applying endpoint conventions like authorization, rate limiting, or CORS to all Trax endpoints:

```csharp
// Require auth on all Trax REST endpoints
app.UseTraxRestApi(configure: group => group
    .RequireAuthorization("AdminPolicy")
    .RequireRateLimiting("fixed"));

// Require auth on the GraphQL endpoint
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

The REST callback receives a `RouteGroupBuilder`, the GraphQL callback receives an `IEndpointConventionBuilder`. Both support `.RequireAuthorization()`, `.RequireRateLimiting()`, `.RequireCors()`, `.AddEndpointFilter<T>()`, and any other endpoint convention.

For global auth that applies to everything (including non-Trax routes), use middleware instead:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

## Named GraphQL Schema

The GraphQL API registers on a **named HotChocolate schema** (`"trax"`) rather than the default unnamed schema. This means it won't conflict with your own `AddGraphQLServer()` calls — both can coexist in the same application at different paths.

## Sample Projects

Working examples are in `Trax.Samples`:

- **`samples/Trax.Samples.Api.Rest`** — Standalone REST API with a sample train, scheduled job, and health check
- **`samples/Trax.Samples.Api.GraphQL`** — Standalone GraphQL API with Banana Cake Pop IDE

Both samples include a `GreetTrain` for testing, a 1-minute scheduled job for query data, and `appsettings.json` pointing to the local PostgreSQL instance.

## SDK Reference

For complete endpoint documentation, request/response schemas, and method signatures:

- [REST API]({{ site.baseurl }}{% link sdk-reference/rest-api.md %}) — endpoint mappings, request/response DTOs, curl examples
- [GraphQL API]({{ site.baseurl }}{% link sdk-reference/graphql-api.md %}) — queries, mutations, HotChocolate setup
- [TrainDiscovery]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-discovery.md %}) — how train discovery works
- [TrainExecution]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-execution.md %}) — queue and run services
