---
layout: default
title: Health Check
parent: REST API
grand_parent: SDK Reference
nav_order: 5
---

# Health Check

`TraxHealthCheck` implements ASP.NET Core's `IHealthCheck` interface to report scheduler system health from database queries. It's registered via the `AddTraxHealthCheck` extension method on `IHealthChecksBuilder`.

The underlying queries are handled by `ITraxHealthService`, a shared service that both the `IHealthCheck` and the [GraphQL `health` query]({{ site.baseurl }}{% link sdk-reference/graphql-api/queries.md %}#health) use. This means both paths return identical metrics from the same code.

## TraxHealthCheck

Queries the database for four metrics on every health check invocation:

| Metric | Source | Description |
|--------|--------|-------------|
| `queueDepth` | `work_queue` table | Work items with status `Queued` — waiting for the scheduler to dispatch |
| `inProgress` | `metadata` table | Executions with `TrainState.InProgress` |
| `failedLastHour` | `metadata` table | Executions with `TrainState.Failed` and `EndTime` within the last hour |
| `deadLetters` | `dead_letters` table | Dead letter entries with status `AwaitingIntervention` |

### Health Status Logic

| Status | Condition |
|--------|-----------|
| `Healthy` | `deadLetters == 0` and `failedLastHour <= 10` |
| `Degraded` | `deadLetters > 0` or `failedLastHour > 10` |

The check never returns `Unhealthy` — database connectivity failures will cause ASP.NET Core's health check middleware to report `Unhealthy` automatically via the unhandled exception path.

## AddTraxHealthCheck

Registers `TraxHealthCheck` with the ASP.NET Core health check system.

### Signature

```csharp
public static IHealthChecksBuilder AddTraxHealthCheck(
    this IHealthChecksBuilder builder,
    string name = "trax",
    params string[] tags
)
```

### Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | No | `"trax"` | The health check name. Appears in the health report output. |
| `tags` | `string[]` | No | `[]` | Tags for filtering health checks. Useful for separating readiness from liveness probes. |

### Returns

`IHealthChecksBuilder` — for continued chaining.

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(Program).Assembly])
        .AddPostgresEffect(connectionString)
);

builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.MapHealthChecks("/trax/health");

app.Run();
```

### With Tags (Kubernetes Probes)

```csharp
builder.Services.AddHealthChecks()
    .AddTraxHealthCheck(name: "trax-scheduler", tags: ["ready"]);

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // no checks — just confirms the process is alive
});
```

## Example Response

`GET /trax/health`

```json
{
  "status": "Healthy",
  "results": {
    "trax": {
      "status": "Healthy",
      "description": "All systems operational",
      "data": {
        "queueDepth": 3,
        "inProgress": 1,
        "failedLastHour": 0,
        "deadLetters": 0
      }
    }
  }
}
```

### Degraded Response

```json
{
  "status": "Degraded",
  "results": {
    "trax": {
      "status": "Degraded",
      "description": "Elevated failures or unresolved dead letters",
      "data": {
        "queueDepth": 15,
        "inProgress": 4,
        "failedLastHour": 12,
        "deadLetters": 3
      }
    }
  }
}
```

## Remarks

- The health check requires a data provider (`AddPostgresEffect`) — it uses `IDataContextProviderFactory` to create a database context for each check.
- All queries use `AsNoTracking()` and `CountAsync` for minimal overhead.
- The JSON response format above requires `ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse` or a custom writer — the default ASP.NET Core health check middleware only returns the status string. Configure a response writer in `HealthCheckOptions` to get the detailed JSON output.
- The health check is in the `Trax.Api` package, not `Trax.Api.Rest`. You can use it independently of the REST endpoints.

## Package

```
dotnet add package Trax.Api
```
