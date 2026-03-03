---
layout: default
title: AddTraxRestApi
parent: REST API
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxRestApi / UseTraxRestApi

Registers REST API services and maps all Trax endpoints as ASP.NET Core minimal API routes.

## AddTraxRestApi

Registers the core API services required by the REST endpoints. Internally calls `AddTraxApi()` from the `Trax.Api` base package.

### Signature

```csharp
public static IServiceCollection AddTraxRestApi(this IServiceCollection services)
```

### Parameters

None. The method requires that `AddTraxEffects` with `AddServiceTrainBus` has already been called — the REST endpoints depend on `ITrainDiscoveryService` and `ITrainExecutionService` provided by the mediator registration.

### Returns

`IServiceCollection` — for continued chaining.

## UseTraxRestApi

Maps all REST API endpoints under a route prefix using `MapGroup`.

### Signature

```csharp
public static WebApplication UseTraxRestApi(
    this WebApplication app,
    string routePrefix = "/trax/api",
    Action<RouteGroupBuilder>? configure = null
)
```

### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `routePrefix` | `string` | No | `"/trax/api"` | The base path for all REST endpoints. All train, scheduler, and query endpoints are mapped under this prefix. |
| `configure` | `Action<RouteGroupBuilder>?` | No | `null` | Optional callback to apply endpoint conventions (authorization, rate limiting, CORS, endpoint filters) to all Trax REST endpoints. |

### Returns

`WebApplication` — for continued chaining.

### Endpoint Groups Mapped

`UseTraxRestApi` maps five endpoint groups under the route prefix:

| Group | Path | Description |
|-------|------|-------------|
| Trains | `{prefix}/trains/*` | Train discovery, queue, and direct execution |
| Scheduler | `{prefix}/scheduler/*` | Manifest trigger, disable, enable, cancel |
| Manifests | `{prefix}/manifests/*` | Read-only manifest queries |
| ManifestGroups | `{prefix}/manifest-groups/*` | Read-only manifest group queries |
| Executions | `{prefix}/executions/*` | Read-only execution queries |

## Prerequisites

The REST API depends on services registered by the effect system and mediator. At minimum:

- `AddTraxEffects` with `AddServiceTrainBus` — provides `ITrainDiscoveryService` and `ITrainExecutionService`
- A data provider (`AddPostgresEffect`) — required for query endpoints and scheduler operations
- `ITraxScheduler` — required for scheduler endpoints (registered by `AddScheduler` or available when the scheduler subsystem is configured)

## Example

### Minimal Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(Program).Assembly])
        .AddPostgresEffect(connectionString)
        .AddJsonEffect()
);

builder.Services.AddTraxRestApi();

var app = builder.Build();

app.UseTraxRestApi(); // endpoints at /trax/api/*

app.Run();
```

### Custom Route Prefix

```csharp
app.UseTraxRestApi("/v1/api");
// endpoints at /v1/api/trains, /v1/api/scheduler/*, etc.
```

### With Authorization and Rate Limiting

```csharp
app.UseTraxRestApi(configure: group => group
    .RequireAuthorization("AdminPolicy")
    .RequireRateLimiting("fixed"));
```

### With Health Check

```csharp
builder.Services.AddTraxRestApi();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxRestApi();
app.MapHealthChecks("/trax/health");
```

### Combined with Scheduler

If the API server also seeds manifests at startup:

```csharp
builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(Program).Assembly])
        .AddPostgresEffect(connectionString)
        .AddJsonEffect()
        .SaveTrainParameters()
        .AddScheduler(scheduler =>
        {
            scheduler.UsePostgresTaskServer();
            scheduler.Schedule<ISyncTrain>(
                "sync-daily",
                new SyncInput { Source = "production" },
                Cron.Daily(hour: 3)
            );
        })
);

builder.Services.AddTraxRestApi();
```

## Package

```
dotnet add package Trax.Api.Rest
```
