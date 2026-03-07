---
layout: default
title: AddTraxJobRunner
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10
---

# AddTraxJobRunner

Registers the minimal services needed to run `JobRunnerTrain` without the full scheduler. Used on the **remote receiver side** — the process that actually executes trains dispatched by a scheduler via `UseRemoteWorkers()`. Also documents `UseTraxRunEndpoint()` for handling synchronous `run` requests from `UseRemoteRun()`.

## Signatures

```csharp
public static IServiceCollection AddTraxJobRunner(
    this IServiceCollection services
)
```

```csharp
public static RouteHandlerBuilder UseTraxJobRunner(
    this IEndpointRouteBuilder app,
    string route = "/trax/execute"
)
```

```csharp
public static RouteHandlerBuilder UseTraxRunEndpoint(
    this IEndpointRouteBuilder app,
    string route = "/trax/run"
)
```

## Parameters

### AddTraxJobRunner

No parameters. Registers the execution pipeline services only.

### UseTraxJobRunner

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/execute"` | The route to map the POST endpoint for queued jobs |

### UseTraxRunEndpoint

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/run"` | The route to map the POST endpoint for synchronous run requests |

## Examples

### Minimal Remote Executor (Queue Only)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");
app.Run();
```

### Remote Executor (Queue + Run)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");  // queue path
app.UseTraxRunEndpoint("/trax/run");    // synchronous run path
app.Run();
```

> **Note:** `UseTraxRunEndpoint()` does not require `AddTraxJobRunner()`. It uses `ITrainExecutionService` which is registered by `AddMediator()`. Only add `AddTraxJobRunner()` if you also need the queue execution path.

### With ASP.NET Authentication

Trax doesn't bake in any auth. The endpoint mapped by `UseTraxJobRunner()` is a standard ASP.NET minimal API endpoint — secure it however you normally would:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();
builder.Services.AddAuthentication(/* your scheme */);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxJobRunner("/trax/execute").RequireAuthorization();
app.Run();
```

### AWS Lambda (via ASP.NET Lambda Hosting)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");
app.Run();
```

## What It Registers

### AddTraxJobRunner

Registers the minimum set of services to run `JobRunnerTrain`:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `SchedulerConfiguration` | Singleton | Empty configuration (no manifests, no polling) |
| `ICancellationRegistry` → `CancellationRegistry` | Singleton | Process-local cancellation tracking |
| `ITraxScheduler` → `TraxScheduler` | Scoped | Runtime scheduler interface |
| `IDormantDependentContext` → `DormantDependentContext` | Scoped | Dependent train context |
| `IJobRunnerTrain` → `JobRunnerTrain` | Scoped | The train execution pipeline |

**Not registered:** ManifestManager, JobDispatcher, polling services, startup service, `LocalWorkerService`. This process only runs trains — it doesn't schedule or dispatch them.

### UseTraxJobRunner

Maps a `POST` endpoint at the specified route that:

1. Reads a `RemoteJobRequest` from the request body
2. Deserializes the input (if present) using the fully-qualified type name
3. Creates a new DI scope and resolves `IJobRunnerTrain`
4. Calls `Run(new RunJobRequest(metadataId, input))`
5. Returns `200 OK` with `{ "metadataId": 42 }` on success
6. Returns `500` on failure

### UseTraxRunEndpoint

Maps a `POST` endpoint at the specified route that handles synchronous run requests from [`UseRemoteRun()`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %}):

1. Reads a `RemoteRunRequest` from the request body (contains train name and input JSON)
2. Resolves `ITrainExecutionService` and calls `RunAsync(trainName, inputJson)`
3. Serializes the train output as JSON
4. Returns `200 OK` with a `RemoteRunResponse` containing the metadata ID, output JSON, and output type
5. On error: returns `200 OK` with `RemoteRunResponse.IsError = true` and the error message (in-band errors to distinguish from infrastructure failures)

## Shared Requirements

The remote process must:

- **Reference the same train assemblies** passed to `AddMediator()` — train types are resolved by fully-qualified name
- **Connect to the same Postgres database** — metadata, manifests, and state are shared across all processes
- **Register the effect system** — `AddTrax()` with `UsePostgres()` is required

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution]({{ site.baseurl }}{% link scheduler/remote-execution.md %}) — architecture overview and deployment models
- [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) — scheduler-side configuration for HTTP dispatch (queue path)
- [UseRemoteRun]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %}) — scheduler-side configuration for remote run execution
- [AddTraxWorker]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-worker.md %}) — standalone worker (poll-based alternative)
