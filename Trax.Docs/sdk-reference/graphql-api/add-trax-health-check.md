---
layout: default
title: AddTraxHealthCheck
description: Reference for AddTraxHealthCheck, an ASP.NET Core health check reporting queued work, running executions, recent failures and dead letters.
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 12
---

# AddTraxHealthCheck

Adds an ASP.NET Core health check that reports on the Trax store: queued work, running executions, failures in the last hour, and dead letters awaiting intervention. It reports the same numbers as the GraphQL [`health`](/docs/sdk-reference/graphql-api/queries#health) query, through one shared service.

## Signature

```csharp
namespace Trax.Api.Extensions;

public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddTraxHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "trax",
        params string[] tags
    );
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `builder` | `IHealthChecksBuilder` | | The builder `AddHealthChecks()` returns |
| `name` | `string` | `"trax"` | The health check's registration name |
| `tags` | `string[]` | none | Tags for filtering in `MapHealthChecks` |

**Returns**: the `IHealthChecksBuilder`, for chaining.

## What it reports

| Status | When |
|--------|------|
| `Healthy` | No dead letter awaits intervention and at most 10 executions failed in the last hour. Description: "All systems operational". |
| `Degraded` | Any dead letter awaits intervention, or more than 10 executions failed in the last hour. Description: "Elevated failures or unresolved dead letters". |

The check itself never returns `Unhealthy`. If the store cannot be queried, the exception propagates and ASP.NET Core reports the check's failure status, `Unhealthy` by default.

Each result carries the counts as data:

| Key | Meaning |
|-----|---------|
| `queueDepth` | Work queue entries queued and not yet dispatched |
| `inProgress` | Executions running now |
| `failedLastHour` | Executions that failed with an end time in the last hour |
| `deadLetters` | Dead letters awaiting intervention |

The counts are read from the store on every call; nothing is cached.

## Requirements

The check resolves `ITraxHealthService`, which `AddTraxApi` registers, and `AddTraxGraphQL` calls `AddTraxApi`. On a host with neither, the check fails when it runs because the service cannot be resolved. A data provider (`UsePostgres`, `UseSqlite` or `UseInMemory`) must be configured.

`ITraxHealthService` (`Trax.Api.Services.HealthCheck`, one method: `Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)`) is hidden from completion. To change how health is computed for both the check and the GraphQL query, register your own implementation after `AddTraxGraphQL`.

## Example

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler());

builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck(tags: "ready");

var app = builder.Build();
app.MapHealthChecks("/trax/health");
```

For the scheduler's own liveness, which fails when the dispatcher stops completing cycles, see [AddTraxSchedulerLiveness](/docs/sdk-reference/scheduler-api/add-trax-scheduler-liveness).

## Package

```
dotnet add package Trax.Api
```

`Trax.Api.GraphQL` depends on it, so a GraphQL host already has it.
