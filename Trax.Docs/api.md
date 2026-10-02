---
layout: default
title: API
description: "Trax.Api and Trax.Api.GraphQL: the GraphQL layer for running and queueing trains, its two execution modes, packages, health check, auth and named schema."
nav_order: 7
section: Packages
---

# API

> **Security.** Trax ships authentication (`Trax.Api.Auth`, `Trax.Api.Auth.ApiKey`) and audit (`Trax.Api.GraphQL.Audit`) packages alongside this library. NO WARRANTY. See [API Security](/docs/api-security) before deploying to production.

Trax.Api adds a programmatic interface to the scheduling and train execution system. It ships as two NuGet packages: a core library and a GraphQL transport powered by HotChocolate.

The API is designed to run on a **separate machine** from the scheduler. The two share a database: the API writes work queue entries and manifest updates, the scheduler polls them and dispatches trains. This means the API server doesn't run polling services or background workers. It's a thin HTTP layer over the shared state.

## Two Execution Modes

| Mode | How It Works | When to Use |
|------|-------------|-------------|
| **Queue** (delegated) | Creates a `WorkQueue` entry in the database. The scheduler picks it up on its next poll cycle and dispatches it on the scheduler machine. | Heavy trains, recurring work, anything that should run on dedicated scheduler infrastructure. |
| **Run** (direct) | Calls `ITrainBus.RunAsync` on the API machine (default) or offloads to a remote endpoint via [`UseRemoteRun()`](/docs/sdk-reference/scheduler-api/use-remote-run). Either way, the call blocks until completion and returns the train output. | Lightweight on-demand trains where you need the result immediately. When using `UseRemoteRun()`, the API machine doesn't need to run the train code locally. |

Trains opt into the GraphQL schema with the [`[TraxQuery]` or `[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) attributes. Only annotated trains get typed fields generated. EF Core entities can also be exposed directly as paginated, filterable, sortable queries using [`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models).

## Quick Setup

```bash
dotnet add package Trax.Api.GraphQL
```

```csharp
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJson()
    )
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL(); // or: AddTraxGraphQL(g => g.AddDbContext<MyDbContext>())
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxGraphQL();  // maps at /trax/graphql; the Nitro IDE opens there in a browser, in Development
app.MapHealthChecks("/trax/health");

app.Run();
```

## Packages

| Package | Description |
|---------|-------------|
| `Trax.Api` | Core library: DTOs, health check, shared service registration |
| `Trax.Api.GraphQL` | HotChocolate schema (queries, mutations, subscriptions) |

`Trax.Api.GraphQL` depends on `Trax.Api`, so you don't need to reference it directly.

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

The API server doesn't need `AddScheduler()`. It needs `AddMediator()` (for train discovery and direct execution) and a data provider (for DB access). The exception is a host that exposes the operations surface (`ExposeOperationQueries()`, `ExposeOperationMutations()`, or persisted operations with the operations namespace on): it needs `IOperationsService`, and with the mutations also `ITraxScheduler` and an `IJobSubmitter`. `AddScheduler()` registers all three, and the host refuses to start without them. The scheduler configuration (`AddScheduler`) runs on the scheduler machine only. A host that also serves the [dashboard](/docs/dashboard) is the exception: the dashboard works through the Scheduler's `IOperationsService`, and `UseTraxDashboard()` refuses to start without `AddScheduler()`.

However, if you want the API to also schedule manifests at startup (like the scheduler does), you can add `AddScheduler()` on the API machine as well. The polling services can be disabled with configuration if you only want startup seeding.

## Health Check

The API includes an ASP.NET Core `IHealthCheck` that queries the database and reports:

- **Queue depth** - work items waiting for dispatch
- **In-progress** - currently executing trains
- **Failed (last hour)** - recent failures
- **Dead letters** - unresolved dead letter entries

In-progress and failed counts are batched into a single database query (via `GroupBy`) to minimize round-trips on each health check poll.

Returns `Healthy` when everything looks normal, `Degraded` when dead letters exist or recent failures exceed a threshold. The same data is available as a [GraphQL query](/docs/sdk-reference/graphql-api/queries#health) for consumers that prefer structured access over the standard health endpoint.

```csharp
builder.Services.AddHealthChecks().AddTraxHealthCheck();
app.MapHealthChecks("/trax/health");
```

## Authentication & Middleware

Trax ships its own authentication packages: [`AddTraxApiKeyAuth`](/docs/sdk-reference/api-auth/add-trax-api-key-auth), [`AddTraxJwtAuth`](/docs/sdk-reference/api-auth/add-trax-jwt-auth) and [`AddTraxOidcAuth`](/docs/sdk-reference/api-auth/add-trax-oidc-auth). Each registers an ASP.NET Core authentication scheme into the combined Trax policy, and gate the endpoint as a whole on the GraphQL builder:

```csharp
builder.Services.AddTraxApiKeyAuth<MyApiKeyResolver>();
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

Use one of them rather than plain ASP.NET Core authentication. Browsers cannot put a header on a WebSocket upgrade, so an API-key or JWT subscription client sends its credential in the `connection_init` payload, and only the Trax schemes read it there. With no Trax token scheme registered, `connection_init` is accepted as it arrives. Cookie authentication (OIDC) is the exception: the browser sends the cookie on the upgrade. See [API Security](/docs/api-security#subscription-authentication).

Gate the endpoint with the builder's `RequireAuthorization()`, not with an endpoint convention on the mapped route. The builder's gate covers HTTP and the socket, and it is the gate the startup [exposure checks](/docs/authorization#required-exposure-posture) honour. `UseTraxGraphQL` still accepts a `configure` callback for other endpoint conventions, such as rate limiting or CORS:

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireRateLimiting("api"));
```

The callback receives an `IEndpointConventionBuilder` and supports `.RequireRateLimiting()`, `.RequireCors()`, `.AddEndpointFilter<T>()`, and any other endpoint convention.

### Per-Train Authorization

Endpoint-level auth answers "can this user access the API?" For finer control, decorate individual train classes with `[TraxAuthorize]`:

```csharp
[TraxAuthorize("Admin")]
[TraxMutation]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

When the API receives a request to run or queue this train, it checks the current user against the policy before executing. A GraphQL-exposed train (`[TraxQuery]`/`[TraxMutation]`) must declare either `[TraxAuthorize]` or `[TraxAllowAnonymous]`, or the host fails at startup; `[TraxAllowAnonymous]` runs with no per-train restriction. See the [Authorization](/docs/authorization) guide for details.

## Named GraphQL Schema

The GraphQL API registers on a **named HotChocolate schema** (`"trax"`) rather than the default unnamed schema. This means it won't conflict with your own `AddGraphQLServer()` calls, and both can coexist in the same application at different paths.

## Sample Projects

The API is demonstrated in three samples, each using a different deployment topology:

- **LocalWorkers (GameServer)** - API and scheduler as separate processes. The GraphQL API handles lightweight trains directly and queues heavy work for the scheduler. See `samples/LocalWorkers/Trax.Samples.GameServer.Api`.
- **DistributedWorkers (EnergyHub)** - API, scheduler, and dashboard in a single hub process. The hub schedules and serves GraphQL but offloads execution to separate worker processes. See `samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub`.
- **EphemeralWorkers (ContentShield)** - API with `UseRemoteWorkers()` dispatches queued mutations directly to an ephemeral Runner via HTTP. No scheduled jobs, no `background_job` table, purely on-demand, serverless-style execution. See `samples/EphemeralWorkers/Trax.Samples.ContentShield.Api`.

All follow the [trains library pattern](/docs/samples). Trains live in a shared library, executables are thin wrappers that pick which capabilities to enable.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution)

## Next Layer

When you need a monitoring UI for inspecting trains, browsing execution history, and managing manifests from a browser, add [Trax.Dashboard](/docs/dashboard).
