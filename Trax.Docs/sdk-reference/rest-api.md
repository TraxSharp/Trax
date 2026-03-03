---
layout: default
title: REST API
parent: SDK Reference
nav_order: 7
has_children: true
---

# REST API

The REST API exposes Trax train execution, scheduler operations, and read-only queries as ASP.NET Core minimal API endpoints. It ships in the `Trax.Api.Rest` NuGet package, which depends on the `Trax.Api` core library for DTOs, health checks, and shared service registration.

The API is designed to run on a **separate machine** from the scheduler. Both share a PostgreSQL database: the API writes work queue entries and manifest updates, the scheduler polls and dispatches. The API server runs no background workers.

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

## Pages

| Page | Description |
|------|-------------|
| [AddTraxRestApi]({{ site.baseurl }}{% link sdk-reference/rest-api/add-trax-rest-api.md %}) | Service registration and endpoint mapping |
| [Train Endpoints]({{ site.baseurl }}{% link sdk-reference/rest-api/train-endpoints.md %}) | Discovery, queue, and direct execution |
| [Scheduler Endpoints]({{ site.baseurl }}{% link sdk-reference/rest-api/scheduler-endpoints.md %}) | Trigger, disable, enable, cancel manifests and groups |
| [Query Endpoints]({{ site.baseurl }}{% link sdk-reference/rest-api/query-endpoints.md %}) | Read-only queries for manifests, groups, and executions |
| [Health Check]({{ site.baseurl }}{% link sdk-reference/rest-api/health-check.md %}) | ASP.NET Core health check integration |
| [DTOs]({{ site.baseurl }}{% link sdk-reference/rest-api/dtos.md %}) | Request, response, and summary record definitions |

## Endpoint Summary

All endpoints are mapped under the route prefix (default: `/trax/api`).

### Train Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `{prefix}/trains` | List all registered trains with input schemas |
| `POST` | `{prefix}/trains/queue` | Queue a train for scheduler dispatch |
| `POST` | `{prefix}/trains/run` | Run a train directly on the API machine |

### Scheduler Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `{prefix}/scheduler/trigger/{externalId}` | Trigger a manifest immediately |
| `POST` | `{prefix}/scheduler/trigger/{externalId}/delayed` | Trigger a manifest after a delay |
| `POST` | `{prefix}/scheduler/schedule-once` | Schedule a one-off execution (501 — not yet implemented) |
| `POST` | `{prefix}/scheduler/disable/{externalId}` | Disable a manifest |
| `POST` | `{prefix}/scheduler/enable/{externalId}` | Enable a manifest |
| `POST` | `{prefix}/scheduler/cancel/{externalId}` | Cancel in-progress executions for a manifest |
| `POST` | `{prefix}/scheduler/groups/{groupId}/trigger` | Trigger all manifests in a group |
| `POST` | `{prefix}/scheduler/groups/{groupId}/cancel` | Cancel all in-progress executions in a group |

### Query Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `{prefix}/manifests` | List manifests (paginated) |
| `GET` | `{prefix}/manifests/{id}` | Get a single manifest by ID |
| `GET` | `{prefix}/manifest-groups` | List manifest groups (paginated) |
| `GET` | `{prefix}/manifest-groups/{id}` | Get a single manifest group by ID |
| `GET` | `{prefix}/executions` | List executions (paginated) |
| `GET` | `{prefix}/executions/{id}` | Get a single execution by ID |

## Package

```
dotnet add package Trax.Api.Rest
```

`Trax.Api.Rest` depends on `Trax.Api` — you don't need to reference `Trax.Api` directly.
